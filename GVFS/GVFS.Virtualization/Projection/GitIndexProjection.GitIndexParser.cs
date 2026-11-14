using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Background;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal partial class GitIndexParser
        {
            public const int PageSize = 512 * 1024;

            private const ushort ExtendedBit = 0x4000;
            private const ushort SkipWorktreeBit = 0x4000;

            private Stream indexStream;
            private byte[] page;
            private int nextByteIndex;

            private GitIndexProjection projection;

            /// <summary>
            /// A single GitIndexEntry instance used for parsing all entries in the index when building the projection
            /// </summary>
            private GitIndexEntry resuableProjectionBuildingIndexEntry = new GitIndexEntry(buildingNewProjection: true);

            /// <summary>
            /// A single GitIndexEntry instance used by the background task thread for parsing all entries in the index
            /// </summary>
            private GitIndexEntry resuableBackgroundTaskThreadIndexEntry = new GitIndexEntry(buildingNewProjection: false);

            public GitIndexParser(GitIndexProjection projection)
            {
                this.projection = projection;
                this.page = new byte[PageSize];
            }

            public enum MergeStage : byte
            {
                NoConflicts = 0,
                CommonAncestor = 1,
                Yours = 2,
                Theirs = 3
            }

            public static void ValidateIndex(ITracer tracer, Stream indexStream)
            {
                GitIndexParser indexParser = new GitIndexParser(null);
                FileSystemTaskResult result = indexParser.ParseIndex(tracer, indexStream, indexParser.resuableProjectionBuildingIndexEntry, ValidateIndexEntry);

                if (result != FileSystemTaskResult.Success)
                {
                    // ValidateIndex should always result in FileSystemTaskResult.Success (or a thrown exception)
                    throw new InvalidOperationException($"{nameof(ValidateIndex)} failed: {result.ToString()}");
                }
            }

            /// <summary>
            /// Count unique directories in the index by scanning entry paths for separators.
            /// Uses the existing index parser to read entries, avoiding a custom index parser.
            /// </summary>
            public static int CountIndexFolders(ITracer tracer, Stream indexStream)
            {
                HashSet<string> dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                GitIndexParser indexParser = new GitIndexParser(null);

                FileSystemTaskResult result = indexParser.ParseIndex(
                    tracer,
                    indexStream,
                    indexParser.resuableProjectionBuildingIndexEntry,
                    entry =>
                    {
                        // Match the same filter as AddIndexEntryToProjection so the
                        // fallback folder count agrees with the mounted projection.
                        if (!((entry.MergeState != MergeStage.CommonAncestor && entry.SkipWorktree) || entry.MergeState == MergeStage.Yours))
                        {
                            return FileSystemTaskResult.Success;
                        }

                        // Extract unique parent directories from the raw path buffer
                        string path = Encoding.UTF8.GetString(entry.PathBuffer, 0, entry.PathLength);
                        int lastSlash = path.LastIndexOf('/');
                        while (lastSlash > 0)
                        {
                            string dir = path.Substring(0, lastSlash);
                            if (!dirs.Add(dir))
                            {
                                break;
                            }

                            lastSlash = dir.LastIndexOf('/');
                        }

                        return FileSystemTaskResult.Success;
                    });

                if (result != FileSystemTaskResult.Success)
                {
                    throw new InvalidOperationException($"{nameof(CountIndexFolders)} failed: {result}");
                }

                return dirs.Count;
            }

            public void RebuildProjection(ITracer tracer, Stream indexStream)
            {
                if (this.projection == null)
                {
                    throw new InvalidOperationException($"{nameof(this.projection)} cannot be null when calling {nameof(this.RebuildProjection)}");
                }

                this.projection.ClearProjectionCaches();
                FileSystemTaskResult result = this.ParseIndex(
                    tracer,
                    indexStream,
                    this.resuableProjectionBuildingIndexEntry,
                    this.AddIndexEntryToProjection);

                if (result != FileSystemTaskResult.Success)
                {
                    // RebuildProjection should always result in FileSystemTaskResult.Success (or a thrown exception)
                    throw new InvalidOperationException($"{nameof(this.RebuildProjection)}: {nameof(GitIndexParser.ParseIndex)} failed to {nameof(this.AddIndexEntryToProjection)}");
                }
            }

            public FileSystemTaskResult AddMissingModifiedFilesAndRemoveThemFromPlaceholderList(
                ITracer tracer,
                Stream indexStream)
            {
                if (this.projection == null)
                {
                    throw new InvalidOperationException($"{nameof(this.projection)} cannot be null when calling {nameof(this.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList)}");
                }

                HashSet<string> filePlaceholders = this.projection.placeholderDatabase.GetAllFilePaths();

                tracer.RelatedEvent(
                    EventLevel.Informational,
                    $"{nameof(this.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList)}_FilePlaceholderCount",
                    new EventMetadata
                    {
                        { "FilePlaceholderCount", filePlaceholders.Count }
                    });

                FileSystemTaskResult result = this.ParseIndex(
                    tracer,
                    indexStream,
                    this.resuableBackgroundTaskThreadIndexEntry,
                    (data) => this.AddEntryToModifiedPathsAndRemoveFromPlaceholdersIfNeeded(data, filePlaceholders));

                if (result != FileSystemTaskResult.Success)
                {
                    return result;
                }

                // Any paths that were not found in the index need to be added to ModifiedPaths
                // and removed from the placeholder list
                foreach (string path in filePlaceholders)
                {
                    result = this.projection.AddModifiedPath(path);
                    if (result != FileSystemTaskResult.Success)
                    {
                        return result;
                    }

                    this.projection.RemoveFromPlaceholderList(path);
                }

                return FileSystemTaskResult.Success;
            }

            private static FileSystemTaskResult ValidateIndexEntry(GitIndexEntry data)
            {
                if (data.PathLength <= 0 || data.PathBuffer[0] == 0)
                {
                    throw new InvalidDataException("Zero-length path found in index");
                }

                return FileSystemTaskResult.Success;
            }

            /// <summary>
            /// Builds the error message for an index that contains a sparse-directory entry.
            /// </summary>
            /// <param name="entryPath">Path of the sparse-directory entry, as stored in the index</param>
            /// <param name="workingDirectoryRoot">Working directory of the mount, or null if it is not known</param>
            /// <param name="projectionIndexBackupPath">Path of the persisted projection index, or null if it is not known</param>
            private static string GetSparseDirectoryEntryErrorMessage(string entryPath, string workingDirectoryRoot, string projectionIndexBackupPath)
            {
                string workingDirectory = workingDirectoryRoot != null ? $"'{workingDirectoryRoot}'" : "the 'src' folder of the enlistment";
                string projectionBackup = projectionIndexBackupPath != null
                    ? $"'{projectionIndexBackupPath}'"
                    : $"'{Path.Combine(GVFSPlatform.Instance.Constants.DotGVFSRoot, ProjectionIndexBackupName)}' in the enlistment root";

                return
                    $"The git index contains the sparse-directory entry '{EscapeForDisplay(entryPath)}'. " +
                    "VFS for Git does not support a sparse index (git config index.sparse). " +
                    "To expand the index to the full format, do these steps:" + Environment.NewLine +
                    "1. If the enlistment is mounted, run 'gvfs unmount'." + Environment.NewLine +
                    $"2. In {workingDirectory}, run 'git -c core.hookspath= config --worktree index.sparse false'." + Environment.NewLine +
                    $"3. In {workingDirectory}, run 'git -c core.virtualfilesystem= -c core.hookspath= -c index.sparse=false update-index --force-write-index'." + Environment.NewLine +
                    $"4. Delete {projectionBackup}." + Environment.NewLine +
                    "5. Run 'gvfs mount'.";
            }

            /// <summary>
            /// Replaces control and format characters with \uXXXX escapes, so that a path read from
            /// the index cannot add lines or terminal escape sequences to a message.
            /// </summary>
            private static string EscapeForDisplay(string value)
            {
                StringBuilder escaped = null;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
                    {
                        if (escaped == null)
                        {
                            escaped = new StringBuilder(value, 0, i, value.Length + 16);
                        }

                        escaped.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        escaped?.Append(c);
                    }
                }

                return escaped?.ToString() ?? value;
            }

            /// <summary>
            /// Throws if <paramref name="data"/> is a sparse-directory entry from a sparse index
            /// (git index.sparse). Such an entry has mode 040000, a tree id, the skip-worktree bit,
            /// and a path that ends in '/'. It stands in for a collapsed subtree.
            /// </summary>
            /// <remarks>
            /// ParseIndex calls this for every entry, before the entry callback. A callback that
            /// received the entry would treat it as a file: the projection would omit every file in
            /// the collapsed subtree and contain a folder with an empty name, and the placeholder scan
            /// would treat every placeholder in the subtree as absent from the index. The check uses
            /// the trailing '/' because ParseIndex reads the mode field only when SupportsFileMode is
            /// true, and it is false on Windows.
            /// </remarks>
            private void FailIfSparseDirectoryEntry(GitIndexEntry data)
            {
                if (data.PathEndsInSlash || data.TypeAndMode.Type == FileType.Directory)
                {
                    string entryPath = Encoding.UTF8.GetString(data.PathBuffer, 0, data.PathLength);
                    throw new SparseIndexNotSupportedException(GetSparseDirectoryEntryErrorMessage(
                        entryPath,
                        this.projection?.context.Enlistment.WorkingDirectoryRoot,
                        this.projection?.projectionIndexBackupPath));
                }
            }

            private FileSystemTaskResult AddIndexEntryToProjection(GitIndexEntry data)
            {
                // Never want to project the common ancestor even if the skip worktree bit is on
                if ((data.MergeState != MergeStage.CommonAncestor && data.SkipWorktree) || data.MergeState == MergeStage.Yours)
                {
                    data.BuildingProjection_ParsePath();
                    this.projection.AddItemFromIndexEntry(data);
                }
                else
                {
                    data.ClearLastParent();
                }

                return FileSystemTaskResult.Success;
            }

            /// <summary>
            /// Adjusts the modifed paths and placeholders list for an index entry.
            /// </summary>
            /// <param name="gitIndexEntry">Index entry</param>
            /// <param name="filePlaceholders">
            /// Dictionary of file placeholders.  AddEntryToModifiedPathsAndRemoveFromPlaceholdersIfNeeded will
            /// remove enties from filePlaceholders as they are found in the index.  After
            /// AddEntryToModifiedPathsAndRemoveFromPlaceholdersIfNeeded is called for all entries in the index
            /// filePlaceholders will contain only those placeholders that are not in the index.
            /// </param>
            private FileSystemTaskResult AddEntryToModifiedPathsAndRemoveFromPlaceholdersIfNeeded(
                GitIndexEntry gitIndexEntry,
                HashSet<string> filePlaceholders)
            {
                gitIndexEntry.BackgroundTask_ParsePath();
                string placeholderRelativePath = gitIndexEntry.BackgroundTask_GetPlatformRelativePath();

                FileSystemTaskResult result = FileSystemTaskResult.Success;

                if (!gitIndexEntry.SkipWorktree)
                {
                    // A git command (e.g. 'git reset --mixed') may have cleared a file's skip worktree bit without
                    // triggering an update to the projection. If git cleared the skip-worktree bit then git will
                    // be responsible for updating the file and we need to:
                    //    - Ensure this file is in GVFS's modified files database
                    //    - Remove this path from the placeholders list (if present)
                    result = this.projection.AddModifiedPath(placeholderRelativePath);

                    if (result == FileSystemTaskResult.Success)
                    {
                        if (filePlaceholders.Remove(placeholderRelativePath))
                        {
                            this.projection.RemoveFromPlaceholderList(placeholderRelativePath);
                        }
                    }
                }
                else
                {
                    filePlaceholders.Remove(placeholderRelativePath);
                }

                return result;
            }

            /// <summary>
            /// Takes an action on a GitIndexEntry using the index in indexStream
            /// </summary>
            /// <param name="indexStream">Stream for reading a git index file</param>
            /// <param name="entryAction">Action to take on each GitIndexEntry from the index</param>
            /// <returns>
            /// FileSystemTaskResult indicating success or failure of the specified action
            /// </returns>
            /// <remarks>
            /// Only the AddToModifiedFiles method because it updates the modified paths file can result
            /// in TryIndexAction returning a FileSystemTaskResult other than Success.  All other actions result in success (or an exception in the
            /// case of a corrupt index). ParseIndex throws SparseIndexNotSupportedException for a sparse
            /// index, before any action receives a sparse-directory entry.
            /// </remarks>
            private FileSystemTaskResult ParseIndex(
                ITracer tracer,
                Stream indexStream,
                GitIndexEntry resuableParsedIndexEntry,
                Func<GitIndexEntry, FileSystemTaskResult> entryAction)
            {
                this.indexStream = indexStream;
                this.indexStream.Position = 0;
                this.ReadNextPage();

                if (this.page[0] != 'D' ||
                    this.page[1] != 'I' ||
                    this.page[2] != 'R' ||
                    this.page[3] != 'C')
                {
                    throw new InvalidDataException("Incorrect magic signature for index: " + string.Join(string.Empty, this.page.Take(4).Select(c => (char)c)));
                }

                this.Skip(4);
                uint indexVersion = this.ReadFromIndexHeader();
                if (indexVersion != 4)
                {
                    throw new InvalidDataException("Unsupported index version: " + indexVersion);
                }

                uint entryCount = this.ReadFromIndexHeader();

                // Don't want to flood the logs on large indexes so only log every 500ms
                const int LoggingTicksThreshold = 500;
                int nextLogTicks = Environment.TickCount + LoggingTicksThreshold;

                SortedFolderEntries.InitializePools(tracer, entryCount);
                LazyUTF8String.InitializePools(tracer, entryCount);

                resuableParsedIndexEntry.ClearLastParent();
                int previousPathLength = 0;

                bool parseMode = GVFSPlatform.Instance.FileSystem.SupportsFileMode;
                FileSystemTaskResult result = FileSystemTaskResult.Success;
                for (int i = 0; i < entryCount; i++)
                {
                    if (parseMode)
                    {
                        this.Skip(26);

                        // 4-bit object type
                        //     valid values in binary are 1000(regular file), 1010(symbolic link) and 1110(gitlink)
                        //     0100(directory) is a sparse-directory entry, which FailIfSparseDirectoryEntry rejects
                        // 3-bit unused
                        // 9-bit unix permission. Only 0755 and 0644 are valid for regular files. (Legacy repos can also contain 664)
                        //     Symbolic links and gitlinks have value 0 in this field.
                        ushort indexFormatTypeAndMode = this.ReadUInt16();

                        FileTypeAndMode typeAndMode = new FileTypeAndMode(indexFormatTypeAndMode);

                        switch (typeAndMode.Type)
                        {
                            case FileType.Regular:
                                if (typeAndMode.Mode != FileMode755 &&
                                    typeAndMode.Mode != FileMode644 &&
                                    typeAndMode.Mode != FileMode664)
                                {
                                    throw new InvalidDataException($"Invalid file mode {typeAndMode.GetModeAsOctalString()} found for regular file in index");
                                }

                                break;

                            case FileType.SymLink:
                            case FileType.GitLink:
                                if (typeAndMode.Mode != 0)
                                {
                                    throw new InvalidDataException($"Invalid file mode {typeAndMode.GetModeAsOctalString()} found for link file({typeAndMode.Type:X}) in index");
                                }

                                break;

                            case FileType.Directory:
                                // A sparse-directory entry. FailIfSparseDirectoryEntry rejects it
                                // with an actionable message after the parser reads its path.
                                if (typeAndMode.Mode != 0)
                                {
                                    throw new InvalidDataException($"Invalid file mode {typeAndMode.GetModeAsOctalString()} found for sparse directory in index");
                                }

                                break;

                            default:
                                throw new InvalidDataException($"Invalid file type {typeAndMode.Type:X} found in index");
                        }

                        resuableParsedIndexEntry.TypeAndMode = typeAndMode;

                        this.Skip(12);
                    }
                    else
                    {
                        this.Skip(40);
                    }

                    this.ReadSha(resuableParsedIndexEntry);

                    ushort flags = this.ReadUInt16();
                    if (flags == 0)
                    {
                        throw new InvalidDataException("Invalid flags found in index");
                    }

                    resuableParsedIndexEntry.MergeState = (MergeStage)((flags >> 12) & 3);
                    bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                    resuableParsedIndexEntry.PathLength = (ushort)(flags & 0xFFF);

                    resuableParsedIndexEntry.SkipWorktree = false;
                    if (isExtended)
                    {
                        ushort extendedFlags = this.ReadUInt16();
                        resuableParsedIndexEntry.SkipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                    }

                    int replaceLength = this.ReadReplaceLength();
                    resuableParsedIndexEntry.ReplaceIndex = previousPathLength - replaceLength;
                    int bytesToRead = resuableParsedIndexEntry.PathLength - resuableParsedIndexEntry.ReplaceIndex + 1;
                    this.ReadPath(resuableParsedIndexEntry, resuableParsedIndexEntry.ReplaceIndex, bytesToRead);
                    previousPathLength = resuableParsedIndexEntry.PathLength;

                    this.FailIfSparseDirectoryEntry(resuableParsedIndexEntry);

                    result = entryAction.Invoke(resuableParsedIndexEntry);
                    if (result != FileSystemTaskResult.Success)
                    {
                        return result;
                    }

                    int curTicks = Environment.TickCount;
                    if (curTicks - nextLogTicks > 0)
                    {
                        tracer.RelatedInfo($"{i}/{entryCount} index entries parsed.");
                        nextLogTicks = curTicks + LoggingTicksThreshold;
                    }
                }

                tracer.RelatedInfo($"Finished parsing {entryCount} index entries.");
                return result;
            }

            private void ReadNextPage()
            {
                // Last page may be smaller than PageSize; partial fill is safe because
                // the parser stops after entryCount entries and never reads stale bytes.
#pragma warning disable CA2022 // Avoid inexact read
                this.indexStream.Read(this.page, 0, PageSize);
#pragma warning restore CA2022
                this.nextByteIndex = 0;
            }

            private int ReadReplaceLength()
            {
                int headerByte = this.ReadByte();
                int offset = headerByte & 0x7f;

                // Terminate the loop when the high bit is no longer set.
                for (int i = 0; (headerByte & 0x80) != 0; i++)
                {
                    headerByte = this.ReadByte();
                    if (headerByte < 0)
                    {
                        throw new EndOfStreamException("Unexpected end of stream while reading git index.");
                    }

                    offset += 1;
                    offset = (offset << 7) + (headerByte & 0x7f);
                }

                return offset;
            }

            private void ReadSha(GitIndexEntry indexEntryData)
            {
                if (this.nextByteIndex + 20 <= PageSize)
                {
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, 0, 20);
                    this.Skip(20);
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = 20 - availableBytes;

                    if (availableBytes > 0)
                    {
                        Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, 0, availableBytes);
                    }

                    this.ReadNextPage();
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.Sha, availableBytes, remainingBytes);
                    this.Skip(remainingBytes);
                }
            }

            private void ReadPath(GitIndexEntry indexEntryData, int replaceIndex, int byteCount)
            {
                if (this.nextByteIndex + byteCount <= PageSize)
                {
                    Buffer.BlockCopy(this.page, this.nextByteIndex, indexEntryData.PathBuffer, replaceIndex, byteCount);
                    this.Skip(byteCount);
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = byteCount - availableBytes;

                    if (availableBytes != 0)
                    {
                        this.ReadPath(indexEntryData, replaceIndex, availableBytes);
                    }

                    this.ReadNextPage();
                    this.ReadPath(indexEntryData, replaceIndex + availableBytes, remainingBytes);
                }
            }

            private uint ReadFromIndexHeader()
            {
                // This code should only get called for parsing the header, so we don't need to worry about wrapping around a page
                uint result = (uint)
                    (this.page[this.nextByteIndex] << 24 |
                    this.page[this.nextByteIndex + 1] << 16 |
                    this.page[this.nextByteIndex + 2] << 8 |
                    this.page[this.nextByteIndex + 3]);
                this.Skip(4);
                return result;
            }

            private ushort ReadUInt16()
            {
                if (this.nextByteIndex + 2 <= PageSize)
                {
                    ushort result = (ushort)
                        (this.page[this.nextByteIndex] << 8 |
                        this.page[this.nextByteIndex + 1]);
                    this.Skip(2);

                    return result;
                }
                else
                {
                    return (ushort)(this.ReadByte() << 8 | this.ReadByte());
                }
            }

            private byte ReadByte()
            {
                if (this.nextByteIndex < PageSize)
                {
                    byte result = this.page[this.nextByteIndex];

                    this.nextByteIndex++;

                    return result;
                }
                else
                {
                    this.ReadNextPage();
                    return this.ReadByte();
                }
            }

            private void Skip(int byteCount)
            {
                if (this.nextByteIndex + byteCount <= PageSize)
                {
                    this.nextByteIndex += byteCount;
                }
                else
                {
                    int availableBytes = PageSize - this.nextByteIndex;
                    int remainingBytes = byteCount - availableBytes;

                    this.ReadNextPage();
                    this.Skip(remainingBytes);
                }
            }
        }
    }
}
