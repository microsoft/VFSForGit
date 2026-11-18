using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// The result of <see cref="SparseDirectoryTreeLookup.FindFile"/>.
        /// </summary>
        internal enum SparseDirectoryFileLookupResult
        {
            Found,
            NotFound,
            TreeUnreadable,
        }

        /// <summary>
        /// Reads the tree objects behind the sparse-directory entries of a sparse index.
        /// </summary>
        /// <remarks>Implementations must be safe to use from more than one thread.</remarks>
        internal interface IProjectionTreeReader
        {
            /// <summary>
            /// Reads a tree from the local object store. Does not download objects.
            /// </summary>
            TreeEnumerationResult TryReadTree(byte[] treeId, TreeEntryVisitor visitor);

            /// <summary>
            /// Downloads a tree into the local object store.
            /// </summary>
            /// <returns>True if the download succeeded</returns>
            bool TryDownloadTree(string treeSha);
        }

        /// <summary>
        /// Reads trees through the libgit2 repository of the mount, and downloads a missing tree
        /// through the object requestor of the mount.
        /// </summary>
        internal class ProjectionTreeReader : IProjectionTreeReader
        {
            private readonly GitRepo repository;
            private readonly GVFSGitObjects gitObjects;
            private readonly CancellationToken cancellationToken;

            /// <param name="cancellationToken">Cancels a download when the mount stops</param>
            public ProjectionTreeReader(GitRepo repository, GVFSGitObjects gitObjects, CancellationToken cancellationToken)
            {
                this.repository = repository;
                this.gitObjects = gitObjects;
                this.cancellationToken = cancellationToken;
            }

            public TreeEnumerationResult TryReadTree(byte[] treeId, TreeEntryVisitor visitor)
            {
                return this.repository.TryEnumerateTree(treeId, visitor);
            }

            public bool TryDownloadTree(string treeSha)
            {
                if (this.gitObjects == null)
                {
                    return false;
                }

                try
                {
                    return this.gitObjects.TryDownloadAndSaveObject(treeSha, this.cancellationToken, GVFSGitObjects.RequestSource.ProjectionBuild)
                        == GitObjects.DownloadAndSaveObjectResult.Success;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Adds the files under a sparse-directory entry to the projection. The result is the same
        /// projection that the full index (one entry for each file) gives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The walk is depth first and in tree order. Git tree order is the same as index order,
        /// so folders and files are added in the order that the full index adds them. Thus folder
        /// names that differ only in case merge as they do for a full index, and the first name wins.
        /// </para>
        /// <para>
        /// A folder is added only when a file under it is added. A full index has no entry for an
        /// empty tree, so an empty tree does not add a folder.
        /// </para>
        /// <para>
        /// A file is not added if the file, or a folder above it, is in the modified paths. See
        /// <see cref="ModifiedPathsFilter"/>.
        /// </para>
        /// <para>
        /// If a tree is not local, the expander downloads it once. If a tree is still missing, is
        /// not a tree, or cannot be read, or if an entry cannot be in a full index, the expander
        /// throws <see cref="SparseIndexExpansionException"/>. The mount never serves a projection
        /// that does not contain every file.
        /// </para>
        /// <para>
        /// The expander is not thread safe. Use it only on the thread that builds the projection.
        /// </para>
        /// </remarks>
        internal class SparseDirectoryExpander
        {
            // A full index cannot hold a longer path: GitIndexEntry has a 4096-byte path buffer.
            internal const int MaxPathLength = 4095;

            private const int ShaLength = 20;
            private const uint FileTypeMask = 0xF000;
            private const uint TreeFileType = 0x4000;
            private const byte GitPathSeparatorByte = (byte)'/';
            private const byte BackslashByte = (byte)'\\';

            private const string MountWithoutSparseIndexSupport =
                "To mount without sparse-index support, run 'git -c core.hookspath= config gvfs.auto-sparse-index false' " +
                "in the 'src' folder of the enlistment. Then run 'gvfs mount', which shows the steps that expand the index.";

            private readonly ITracer tracer;
            private readonly bool platformSupportsFileMode;
            private readonly bool useWindowsNameRules;
            private readonly SparseFolderData rootSparseFolder;
            private readonly Dictionary<string, FileTypeAndMode> nonDefaultFileTypesAndModes;
            private readonly TreeEntryVisitor visitor;
            private readonly List<ChildEntry> children = new List<ChildEntry>();
            private readonly List<TreeFrame> childFrames = new List<TreeFrame>();
            private readonly List<TreeFrame> framesWithoutFolder = new List<TreeFrame>();
            private readonly Stack<TreeFrame> stack = new Stack<TreeFrame>();
            private readonly byte[] shaBuffer = new byte[ShaLength];
            private readonly Stopwatch expansionTime = new Stopwatch();

            private byte[] childIds = new byte[ShaLength * 64];
            private Func<IEnumerable<string>> getModifiedPaths;
            private ModifiedPathsFilter modifiedPathsFilter;

            // Set during Expand
            private FolderData rootFolderData;
            private GitIndexEntry sparseDirectoryEntry;
            private TreeFrame currentFrame;

            public SparseDirectoryExpander(
                ITracer tracer,
                IProjectionTreeReader treeReader,
                SparseFolderData rootSparseFolder,
                Dictionary<string, FileTypeAndMode> nonDefaultFileTypesAndModes)
            {
                this.tracer = tracer;
                this.TreeReader = treeReader;
                this.platformSupportsFileMode = GVFSPlatform.Instance.FileSystem.SupportsFileMode;
                this.useWindowsNameRules = GVFSPlatform.GVFSPlatformConstants.PathSeparator == '\\';
                this.rootSparseFolder = rootSparseFolder;
                this.nonDefaultFileTypesAndModes = nonDefaultFileTypesAndModes;
                this.visitor = this.AddChildEntry;
            }

            public IProjectionTreeReader TreeReader { get; private set; }

            public int SparseDirectoryCount { get; private set; }
            public int SkippedSparseDirectoryCount { get; private set; }
            public int TreesRead { get; private set; }
            public int TreesDownloaded { get; private set; }
            public int FilesAdded { get; private set; }
            public int FilesSkippedAsModified { get; private set; }

            public long ElapsedMilliseconds
            {
                get { return this.expansionTime.ElapsedMilliseconds; }
            }

            /// <summary>
            /// Resets the statistics, and sets the source of the modified paths for the next build.
            /// </summary>
            /// <param name="getModifiedPaths">
            /// Returns the modified paths, in git format. Called at most once, when the build
            /// expands its first sparse-directory entry. Null if there are no modified paths.
            /// </param>
            public void StartBuild(Func<IEnumerable<string>> getModifiedPaths)
            {
                this.getModifiedPaths = getModifiedPaths;
                this.modifiedPathsFilter = null;
                this.SparseDirectoryCount = 0;
                this.SkippedSparseDirectoryCount = 0;
                this.TreesRead = 0;
                this.TreesDownloaded = 0;
                this.FilesAdded = 0;
                this.FilesSkippedAsModified = 0;
                this.expansionTime.Reset();
            }

            public void AddMetadata(EventMetadata metadata)
            {
                metadata.Add("SparseDirectoryCount", this.SparseDirectoryCount);
                metadata.Add("SkippedSparseDirectoryCount", this.SkippedSparseDirectoryCount);
                metadata.Add("SparseTreesRead", this.TreesRead);
                metadata.Add("SparseTreesDownloaded", this.TreesDownloaded);
                metadata.Add("SparseFilesAdded", this.FilesAdded);
                metadata.Add("SparseFilesSkippedAsModified", this.FilesSkippedAsModified);
                metadata.Add("SparseExpansionMilliseconds", this.ElapsedMilliseconds);
            }

            /// <summary>
            /// Adds the files under a sparse-directory entry to the projection.
            /// </summary>
            /// <param name="rootFolderData">The root folder of the projection</param>
            /// <param name="entry">
            /// The sparse-directory entry. Its path parts must contain the path of the collapsed
            /// folder (see <see cref="GitIndexEntry.BuildingProjection_ParseSparseDirectoryPath"/>).
            /// </param>
            public void Expand(FolderData rootFolderData, GitIndexEntry entry)
            {
                this.expansionTime.Start();
                try
                {
                    this.SparseDirectoryCount++;

                    if (this.modifiedPathsFilter == null)
                    {
                        this.modifiedPathsFilter = ModifiedPathsFilter.Create(this.getModifiedPaths?.Invoke());
                    }

                    bool trackPaths = this.platformSupportsFileMode || !this.modifiedPathsFilter.IsEmpty;
                    string rootGitPath = trackPaths ? entry.BuildingProjection_GetGitRelativePath() : null;
                    bool checkModifiedPaths = false;
                    if (!this.modifiedPathsFilter.IsEmpty)
                    {
                        if (this.modifiedPathsFilter.ContainsFolderOrParentFolder(rootGitPath))
                        {
                            this.SkippedSparseDirectoryCount++;
                            return;
                        }

                        checkModifiedPaths = this.modifiedPathsFilter.HasEntriesUnder(rootGitPath);
                    }

                    byte[] rootTreeId = new byte[ShaLength];
                    Buffer.BlockCopy(entry.Sha, 0, rootTreeId, 0, ShaLength);

                    this.rootFolderData = rootFolderData;
                    this.sparseDirectoryEntry = entry;
                    this.stack.Clear();
                    this.stack.Push(new TreeFrame(
                        parent: null,
                        name: entry.BuildingProjection_GetChildName(),
                        treeId: rootTreeId,
                        pathLength: entry.PathLength - 1,
                        inclusion: this.GetSparseDirectoryInclusion(entry),
                        gitPath: this.NeedsGitPath(checkModifiedPaths) ? rootGitPath : null,
                        checkModifiedPaths: checkModifiedPaths));

                    while (this.stack.Count > 0)
                    {
                        this.ExpandTree(this.stack.Pop());
                    }
                }
                finally
                {
                    this.rootFolderData = null;
                    this.sparseDirectoryEntry = null;
                    this.currentFrame = null;
                    this.stack.Clear();
                    this.childFrames.Clear();
                    this.framesWithoutFolder.Clear();
                    this.children.Clear();
                    this.expansionTime.Stop();
                }
            }

            /// <summary>
            /// Replaces the tree reader. For unit tests only.
            /// </summary>
            internal void SetTreeReaderForTests(IProjectionTreeReader reader)
            {
                this.TreeReader = reader;
            }

            private static string GetChildGitPath(string parentGitPath, LazyUTF8String name)
            {
                return parentGitPath + GVFSConstants.GitPathSeparatorString + name.GetString();
            }

            /// <summary>
            /// True if a tree entry name is ".git" for git on Windows (core.protectNTFS): the name
            /// with trailing dots and spaces removed is ".git", or the name is the short name "git~1".
            /// </summary>
            private static bool IsWindowsDotGitName(string name)
            {
                return name.TrimEnd('.', ' ').Equals(GVFSConstants.DotGit.Root, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("git~1", StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// True if a folder needs its git path: to match modified paths under it, or to
            /// record the file modes of its files.
            /// </summary>
            private bool NeedsGitPath(bool checkModifiedPaths)
            {
                return checkModifiedPaths || this.platformSupportsFileMode;
            }

            private void ExpandTree(TreeFrame frame)
            {
                this.ReadTree(frame);

                // Add the children in tree order. Push the child trees in reverse order, so that
                // the walk reads them in tree order.
                this.childFrames.Clear();
                for (int i = 0; i < this.children.Count; i++)
                {
                    ChildEntry child = this.children[i];
                    int childPathLength = frame.PathLength + 1 + child.NameLength;
                    if (childPathLength > MaxPathLength)
                    {
                        this.ThrowExpansionException(
                            frame,
                            folder => $"The folder '{folder}' in the sparse index has a path that is longer than {MaxPathLength} bytes. Git cannot add this path to an index.");
                    }

                    string childGitPath = frame.GitPath != null ? GetChildGitPath(frame.GitPath, child.Name) : null;
                    bool isTree = (child.FileMode & FileTypeMask) == TreeFileType;

                    if (isTree)
                    {
                        bool checkModifiedPaths = false;
                        if (frame.CheckModifiedPaths)
                        {
                            if (this.modifiedPathsFilter.ContainsFolder(childGitPath))
                            {
                                continue;
                            }

                            checkModifiedPaths = this.modifiedPathsFilter.HasEntriesUnder(childGitPath);
                        }

                        byte[] childTreeId = new byte[ShaLength];
                        Buffer.BlockCopy(this.childIds, i * ShaLength, childTreeId, 0, ShaLength);
                        this.childFrames.Add(new TreeFrame(
                            frame,
                            child.Name,
                            childTreeId,
                            childPathLength,
                            this.GetChildInclusion(frame.Inclusion, child.Name),
                            this.NeedsGitPath(checkModifiedPaths) ? childGitPath : null,
                            checkModifiedPaths));
                    }
                    else
                    {
                        if (frame.CheckModifiedPaths && this.modifiedPathsFilter.ContainsFile(childGitPath))
                        {
                            this.FilesSkippedAsModified++;
                            continue;
                        }

                        FileTypeAndMode typeAndMode = default(FileTypeAndMode);
                        if (this.platformSupportsFileMode)
                        {
                            typeAndMode = new FileTypeAndMode((ushort)child.FileMode);
                            this.ValidateFileTypeAndMode(frame, typeAndMode, childGitPath);
                        }

                        Buffer.BlockCopy(this.childIds, i * ShaLength, this.shaBuffer, 0, ShaLength);
                        this.GetOrAddFolder(frame).AddChildFile(child.Name, this.shaBuffer);
                        this.FilesAdded++;

                        if (this.platformSupportsFileMode &&
                            (typeAndMode.Type != FileType.Regular || typeAndMode.Mode != FileMode644))
                        {
                            this.nonDefaultFileTypesAndModes.Add(childGitPath, typeAndMode);
                        }
                    }
                }

                for (int i = this.childFrames.Count - 1; i >= 0; i--)
                {
                    this.stack.Push(this.childFrames[i]);
                }
            }

            /// <summary>
            /// Reads the entries of the tree of <paramref name="frame"/> into <see cref="children"/>.
            /// </summary>
            private void ReadTree(TreeFrame frame)
            {
                this.currentFrame = frame;
                this.children.Clear();
                TreeEnumerationResult result = this.TreeReader.TryReadTree(frame.TreeId, this.visitor);

                string treeSha = null;
                if (result == TreeEnumerationResult.MissingTree)
                {
                    treeSha = SHA1Util.HexStringFromBytes(frame.TreeId);
                    if (this.TreeReader.TryDownloadTree(treeSha))
                    {
                        this.TreesDownloaded++;
                        this.children.Clear();
                        result = this.TreeReader.TryReadTree(frame.TreeId, this.visitor);
                    }
                }

                if (result != TreeEnumerationResult.Success)
                {
                    this.children.Clear();
                    treeSha = treeSha ?? SHA1Util.HexStringFromBytes(frame.TreeId);
                    string folderPath = this.GetFolderPathForDisplay(frame);

                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("TreeSha", treeSha);
                    metadata.Add("Result", result.ToString());
                    metadata.Add("FolderPath", folderPath);
                    this.tracer.RelatedError(metadata, $"{nameof(SparseDirectoryExpander)}: Failed to read the tree of a sparse directory");

                    string nextStep = result == TreeEnumerationResult.MissingTree || result == TreeEnumerationResult.RepositoryUnavailable
                        ? "Make sure that the network connection works, and run 'gvfs mount' again. If the problem continues: "
                        : string.Empty;
                    throw new SparseIndexExpansionException(
                        $"Cannot read tree {treeSha} for the folder '{GitIndexParser.EscapeForDisplay(folderPath)}' in the sparse index ({result}). " +
                        "VFS for Git cannot project the files in this folder. " + nextStep + MountWithoutSparseIndexSupport);
                }

                this.TreesRead++;
            }

            /// <summary>
            /// The visitor for <see cref="IProjectionTreeReader.TryReadTree"/>. Copies an entry into
            /// <see cref="children"/>. The projection does not change until the read succeeds.
            /// </summary>
            private unsafe void AddChildEntry(ReadOnlySpan<byte> name, ReadOnlySpan<byte> objectId, uint fileMode)
            {
                if (objectId.Length != ShaLength)
                {
                    int idLength = objectId.Length;
                    this.ThrowExpansionException(this.currentFrame, folder => $"The folder '{folder}' in the sparse index has a tree entry with a {idLength}-byte object id.");
                }

                this.ValidateName(name);

                int idOffset = this.children.Count * ShaLength;
                if (idOffset + ShaLength > this.childIds.Length)
                {
                    Array.Resize(ref this.childIds, this.childIds.Length * 2);
                }

                objectId.CopyTo(new Span<byte>(this.childIds, idOffset, ShaLength));

                LazyUTF8String childName;
                fixed (byte* namePtr = name)
                {
                    childName = LazyUTF8String.FromByteArray(namePtr, name.Length);
                }

                this.children.Add(new ChildEntry(childName, name.Length, fileMode));
            }

            /// <summary>
            /// Throws if a tree entry name cannot be a path component of a full index. Git does not
            /// add such a path to an index, so a full index cannot project it.
            /// </summary>
            private void ValidateName(ReadOnlySpan<byte> name)
            {
                bool isValid = name.Length > 0;
                for (int i = 0; isValid && i < name.Length; i++)
                {
                    byte b = name[i];
                    isValid = b != GitPathSeparatorByte && b != 0 && !(this.useWindowsNameRules && b == BackslashByte);
                }

                string nameString = null;
                if (isValid && !name.SequenceEqual("."u8) && !name.SequenceEqual(".."u8))
                {
                    nameString = Encoding.UTF8.GetString(name);
                    isValid = !nameString.Equals(GVFSConstants.DotGit.Root, StringComparison.OrdinalIgnoreCase) &&
                        !(this.useWindowsNameRules && IsWindowsDotGitName(nameString));
                }
                else
                {
                    isValid = false;
                }

                if (!isValid)
                {
                    string invalidName = nameString ?? Encoding.UTF8.GetString(name);
                    this.ThrowExpansionException(
                        this.currentFrame,
                        folder => $"The folder '{folder}' in the sparse index has a tree entry with the name '{GitIndexParser.EscapeForDisplay(invalidName)}'. Git cannot add this name to an index.");
                }
            }

            /// <summary>
            /// Throws if a file type and mode cannot be in a full index. These are the checks that
            /// GitIndexParser.ParseIndex does for an entry of a full index.
            /// </summary>
            private void ValidateFileTypeAndMode(TreeFrame frame, FileTypeAndMode typeAndMode, string gitPath)
            {
                bool isValid;
                switch (typeAndMode.Type)
                {
                    case FileType.Regular:
                        isValid = typeAndMode.Mode == FileMode755 || typeAndMode.Mode == FileMode644 || typeAndMode.Mode == FileMode664;
                        break;

                    case FileType.SymLink:
                    case FileType.GitLink:
                        isValid = typeAndMode.Mode == 0;
                        break;

                    default:
                        isValid = false;
                        break;
                }

                if (!isValid)
                {
                    this.ThrowExpansionException(
                        frame,
                        folder => $"The file '{GitIndexParser.EscapeForDisplay(gitPath)}' in the folder '{folder}' of the sparse index has the invalid type {typeAndMode.Type} and mode {typeAndMode.GetModeAsOctalString()}.");
                }
            }

            /// <summary>
            /// Logs and throws <see cref="SparseIndexExpansionException"/>.
            /// </summary>
            /// <param name="buildMessage">Builds the message from the escaped folder path of <paramref name="frame"/></param>
            private void ThrowExpansionException(TreeFrame frame, Func<string, string> buildMessage)
            {
                string message = buildMessage(GitIndexParser.EscapeForDisplay(this.GetFolderPathForDisplay(frame))) +
                    " VFS for Git cannot project the files in this folder. " + MountWithoutSparseIndexSupport;

                EventMetadata metadata = CreateEventMetadata();
                metadata.Add(TracingConstants.MessageKey.ErrorMessage, message);
                this.tracer.RelatedError(metadata, $"{nameof(SparseDirectoryExpander)}: Invalid tree in a sparse directory");

                throw new SparseIndexExpansionException(message);
            }

            /// <summary>
            /// Returns the folder of <paramref name="frame"/>. Adds the folder, and the folders
            /// above it, to the projection if they are not there.
            /// </summary>
            private FolderData GetOrAddFolder(TreeFrame frame)
            {
                if (frame.Folder != null)
                {
                    return frame.Folder;
                }

                // Find the frames above this one that have no folder yet, without recursion
                this.framesWithoutFolder.Clear();
                TreeFrame current = frame;
                while (current != null && current.Folder == null)
                {
                    this.framesWithoutFolder.Add(current);
                    current = current.Parent;
                }

                FolderData folder = current?.Folder;
                for (int i = this.framesWithoutFolder.Count - 1; i >= 0; i--)
                {
                    TreeFrame frameToAdd = this.framesWithoutFolder[i];
                    folder = frameToAdd.Parent == null
                        ? this.GetOrAddSparseDirectoryFolder()
                        : folder.ChildEntries.GetOrAddFolder(frameToAdd.Name, frameToAdd.Inclusion.Included);
                    frameToAdd.Folder = folder;
                }

                this.framesWithoutFolder.Clear();
                return frame.Folder;
            }

            /// <summary>
            /// Adds the folders of the sparse-directory entry path to the projection, as
            /// AddFileToTree adds the folders of a file path.
            /// </summary>
            private FolderData GetOrAddSparseDirectoryFolder()
            {
                GitIndexEntry entry = this.sparseDirectoryEntry;
                FolderData folder = this.rootFolderData;
                for (int pathIndex = 0; pathIndex < entry.BuildingProjection_NumParts; ++pathIndex)
                {
                    folder = folder.ChildEntries.GetOrAddFolder(entry.BuildingProjection_PathParts, pathIndex, folder.IsIncluded, this.rootSparseFolder);
                }

                return folder;
            }

            private string GetFolderPathForDisplay(TreeFrame frame)
            {
                if (frame.GitPath != null)
                {
                    return frame.GitPath;
                }

                List<string> names = new List<string>();
                for (TreeFrame current = frame; current.Parent != null; current = current.Parent)
                {
                    names.Add(current.Name.GetString());
                }

                names.Add(this.sparseDirectoryEntry.BuildingProjection_GetGitRelativePath());
                names.Reverse();
                return string.Join(GVFSConstants.GitPathSeparatorString, names);
            }

            /// <summary>
            /// Computes the inclusion of the collapsed folder in the gvfs sparse folders, as
            /// <see cref="SortedFolderEntries.GetOrAddFolder(LazyUTF8String[], int, bool, SparseFolderData)"/> does.
            /// </summary>
            private FrameInclusion GetSparseDirectoryInclusion(GitIndexEntry entry)
            {
                if (this.rootSparseFolder.Children.Count == 0)
                {
                    return FrameInclusion.AllIncluded;
                }

                SparseFolderData node = this.rootSparseFolder;
                for (int i = 0; i < entry.BuildingProjection_NumParts; i++)
                {
                    if (node.IsRecursive)
                    {
                        return FrameInclusion.AllIncluded;
                    }

                    if (!node.Children.TryGetValue(entry.BuildingProjection_PathParts[i].GetString(), out node))
                    {
                        return FrameInclusion.Excluded;
                    }
                }

                return new FrameInclusion(included: true, recursive: node.IsRecursive, node: node);
            }

            /// <summary>
            /// Computes the inclusion of a child folder from the inclusion of its parent folder. The
            /// result is the same as the walk from the root in
            /// <see cref="SortedFolderEntries.GetOrAddFolder(LazyUTF8String[], int, bool, SparseFolderData)"/>.
            /// </summary>
            private FrameInclusion GetChildInclusion(FrameInclusion parent, LazyUTF8String childName)
            {
                if (this.rootSparseFolder.Children.Count == 0 || (parent.Included && parent.Recursive))
                {
                    return FrameInclusion.AllIncluded;
                }

                if (!parent.Included)
                {
                    return FrameInclusion.Excluded;
                }

                if (parent.Node.Children.TryGetValue(childName.GetString(), out SparseFolderData childNode))
                {
                    return new FrameInclusion(included: true, recursive: childNode.IsRecursive, node: childNode);
                }

                return FrameInclusion.Excluded;
            }

            private readonly struct ChildEntry
            {
                public ChildEntry(LazyUTF8String name, int nameLength, uint fileMode)
                {
                    this.Name = name;
                    this.NameLength = nameLength;
                    this.FileMode = fileMode;
                }

                public LazyUTF8String Name { get; }

                // The length of the name in UTF-8 bytes
                public int NameLength { get; }
                public uint FileMode { get; }
            }

            private class TreeFrame
            {
                public TreeFrame(TreeFrame parent, LazyUTF8String name, byte[] treeId, int pathLength, FrameInclusion inclusion, string gitPath, bool checkModifiedPaths)
                {
                    this.Parent = parent;
                    this.Name = name;
                    this.TreeId = treeId;
                    this.PathLength = pathLength;
                    this.Inclusion = inclusion;
                    this.GitPath = gitPath;
                    this.CheckModifiedPaths = checkModifiedPaths;
                }

                public TreeFrame Parent { get; }
                public LazyUTF8String Name { get; }
                public byte[] TreeId { get; }

                // The length of the git path of the folder in UTF-8 bytes
                public int PathLength { get; }
                public FrameInclusion Inclusion { get; }

                // The git path of the folder. Null unless the expander needs paths for this subtree.
                public string GitPath { get; }

                // True if a modified path is under this folder
                public bool CheckModifiedPaths { get; }

                // Null until the expander adds a file under this folder
                public FolderData Folder { get; set; }
            }
        }

        /// <summary>
        /// Finds whether a file is in the tree of a sparse-directory entry. The placeholder scan
        /// uses it to decide if a placeholder under a collapsed folder is in the index.
        /// </summary>
        /// <remarks>
        /// Name matches use the path comparer, as the placeholder scan does for a full index. The
        /// lookup caches each tree that it reads. Use one instance for one scan, on one thread.
        /// </remarks>
        internal class SparseDirectoryTreeLookup
        {
            private readonly IProjectionTreeReader treeReader;
            private readonly StringComparer pathComparer = GVFSPlatform.Instance.Constants.PathComparer;
            private readonly Dictionary<string, List<TreeChild>> trees = new Dictionary<string, List<TreeChild>>(StringComparer.OrdinalIgnoreCase);

            public SparseDirectoryTreeLookup(IProjectionTreeReader treeReader)
            {
                this.treeReader = treeReader;
            }

            /// <param name="treeId">The tree of the sparse-directory entry</param>
            /// <param name="relativePathParts">The path of the file under the collapsed folder, one name for each part</param>
            public SparseDirectoryFileLookupResult FindFile(byte[] treeId, string[] relativePathParts)
            {
                return this.FindFile(treeId, relativePathParts, 0);
            }

            private SparseDirectoryFileLookupResult FindFile(byte[] treeId, string[] parts, int partIndex)
            {
                List<TreeChild> children = this.ReadTree(treeId);
                if (children == null)
                {
                    return SparseDirectoryFileLookupResult.TreeUnreadable;
                }

                bool isLastPart = partIndex == parts.Length - 1;
                SparseDirectoryFileLookupResult result = SparseDirectoryFileLookupResult.NotFound;
                foreach (TreeChild child in children)
                {
                    if (child.IsTree == isLastPart || !this.pathComparer.Equals(child.Name, parts[partIndex]))
                    {
                        continue;
                    }

                    if (isLastPart)
                    {
                        return SparseDirectoryFileLookupResult.Found;
                    }

                    // Folder names that differ only in case are one folder in the projection, so
                    // look in each matching tree.
                    SparseDirectoryFileLookupResult childResult = this.FindFile(child.Id, parts, partIndex + 1);
                    if (childResult == SparseDirectoryFileLookupResult.Found)
                    {
                        return childResult;
                    }

                    if (childResult == SparseDirectoryFileLookupResult.TreeUnreadable)
                    {
                        result = childResult;
                    }
                }

                return result;
            }

            private List<TreeChild> ReadTree(byte[] treeId)
            {
                string treeSha = SHA1Util.HexStringFromBytes(treeId);
                if (this.trees.TryGetValue(treeSha, out List<TreeChild> cached))
                {
                    return cached;
                }

                List<TreeChild> children = new List<TreeChild>();
                TreeEntryVisitor visitor = (name, objectId, fileMode) =>
                    children.Add(new TreeChild(Encoding.UTF8.GetString(name), objectId.ToArray(), (fileMode & 0xF000) == 0x4000));

                TreeEnumerationResult result = this.treeReader.TryReadTree(treeId, visitor);
                if (result == TreeEnumerationResult.MissingTree && this.treeReader.TryDownloadTree(treeSha))
                {
                    children.Clear();
                    result = this.treeReader.TryReadTree(treeId, visitor);
                }

                if (result != TreeEnumerationResult.Success)
                {
                    return null;
                }

                this.trees.Add(treeSha, children);
                return children;
            }

            private readonly struct TreeChild
            {
                public TreeChild(string name, byte[] id, bool isTree)
                {
                    this.Name = name;
                    this.Id = id;
                    this.IsTree = isTree;
                }

                public string Name { get; }
                public byte[] Id { get; }
                public bool IsTree { get; }
            }
        }

        /// <summary>
        /// The inclusion of a folder in the gvfs sparse folders (<see cref="ISparseCollection"/>),
        /// as the expander walks down a tree.
        /// </summary>
        internal readonly struct FrameInclusion
        {
            public static readonly FrameInclusion AllIncluded = new FrameInclusion(included: true, recursive: true, node: null);
            public static readonly FrameInclusion Excluded = new FrameInclusion(included: false, recursive: false, node: null);

            public FrameInclusion(bool included, bool recursive, SparseFolderData node)
            {
                this.Included = included;
                this.Recursive = recursive;
                this.Node = node;
            }

            /// <summary>True if the folder is included (<see cref="FolderData.IsIncluded"/>).</summary>
            public bool Included { get; }

            /// <summary>True if every folder under this folder is included.</summary>
            public bool Recursive { get; }

            /// <summary>The sparse folder node that matches this folder. Null if Recursive or not Included.</summary>
            public SparseFolderData Node { get; }
        }

        /// <summary>
        /// A snapshot of the modified paths, for the expansion of a sparse directory.
        /// </summary>
        /// <remarks>
        /// The virtual file system hook reports the modified paths to git. For a full index, git
        /// clears the skip-worktree bit of each entry whose path is a modified file path, and of
        /// each entry under a modified folder path ("folder/"). Matches ignore case on a
        /// case-insensitive file system. The projection does not contain an entry without the
        /// skip-worktree bit. The filter gives the same result for the files under a sparse directory.
        /// </remarks>
        internal class ModifiedPathsFilter
        {
            private static readonly ModifiedPathsFilter Empty = new ModifiedPathsFilter();

            private readonly HashSet<string> files;
            private readonly HashSet<string> folders;

            // Every entry, sorted with the path comparer. The entries under a folder are contiguous.
            private readonly List<string> sortedEntries;
            private readonly StringComparison pathComparison;

            private ModifiedPathsFilter()
            {
                this.IsEmpty = true;
            }

            private ModifiedPathsFilter(IEnumerable<string> modifiedPaths)
            {
                StringComparer comparer = GVFSPlatform.Instance.Constants.PathComparer;
                this.pathComparison = GVFSPlatform.Instance.Constants.PathComparison;
                this.files = new HashSet<string>(comparer);
                this.folders = new HashSet<string>(comparer);
                this.sortedEntries = new List<string>();

                foreach (string path in modifiedPaths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    this.sortedEntries.Add(path);
                    if (path[path.Length - 1] == GVFSConstants.GitPathSeparator)
                    {
                        this.folders.Add(path.Substring(0, path.Length - 1));
                    }
                    else
                    {
                        this.files.Add(path);
                    }
                }

                this.sortedEntries.Sort(comparer);
                this.IsEmpty = this.sortedEntries.Count == 0;
            }

            public bool IsEmpty { get; }

            /// <param name="modifiedPaths">Modified paths in git format. A folder path ends in '/'. Can be null.</param>
            public static ModifiedPathsFilter Create(IEnumerable<string> modifiedPaths)
            {
                return modifiedPaths == null ? Empty : new ModifiedPathsFilter(modifiedPaths);
            }

            /// <summary>True if <paramref name="gitPath"/> is a modified file path.</summary>
            public bool ContainsFile(string gitPath)
            {
                return !this.IsEmpty && this.files.Contains(gitPath);
            }

            /// <summary>True if <paramref name="gitPath"/>, without a trailing '/', is a modified folder path.</summary>
            public bool ContainsFolder(string gitPath)
            {
                return !this.IsEmpty && this.folders.Contains(gitPath);
            }

            /// <summary>
            /// True if <paramref name="gitPath"/>, or a folder above it, is a modified folder path.
            /// </summary>
            public bool ContainsFolderOrParentFolder(string gitPath)
            {
                if (this.IsEmpty || this.folders.Count == 0)
                {
                    return false;
                }

                for (int separatorIndex = gitPath.IndexOf(GVFSConstants.GitPathSeparator);
                    separatorIndex > 0;
                    separatorIndex = gitPath.IndexOf(GVFSConstants.GitPathSeparator, separatorIndex + 1))
                {
                    if (this.folders.Contains(gitPath.Substring(0, separatorIndex)))
                    {
                        return true;
                    }
                }

                return this.folders.Contains(gitPath);
            }

            /// <summary>
            /// True if a modified path is under the folder <paramref name="folderGitPath"/>.
            /// </summary>
            public bool HasEntriesUnder(string folderGitPath)
            {
                if (this.IsEmpty)
                {
                    return false;
                }

                string prefix = folderGitPath + GVFSConstants.GitPathSeparatorString;
                int index = this.sortedEntries.BinarySearch(prefix, GVFSPlatform.Instance.Constants.PathComparer);
                if (index < 0)
                {
                    index = ~index;
                }

                return index < this.sortedEntries.Count && this.sortedEntries[index].StartsWith(prefix, this.pathComparison);
            }
        }
    }
}
