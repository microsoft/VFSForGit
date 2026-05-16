using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Prefetch.Git
{
    public class DiffHelper
    {
        private const string AreaPath = nameof(DiffHelper);

        private ITracer tracer;
        private HashSet<string> exactFileList;
        private List<string> patternList;
        private List<string> folderList;
        // The staged collections are keyed by the case-insensitive PathComparer on
        // case-insensitive platforms so that two paths differing only in case map to
        // the same entry. The dictionary value stores the original casing of the
        // first path inserted, which case-rename detection compares against the
        // incoming path to decide whether the collision is a rename or a true
        // duplicate. Dictionary lookups keep this O(1); a HashSet would force a
        // linear scan to recover the stored casing.
        private Dictionary<string, string> filesAdded = new Dictionary<string, string>(GVFSPlatform.Instance.Constants.PathComparer);

        private Dictionary<string, DiffTreeResult> stagedDirectoryOperations = new Dictionary<string, DiffTreeResult>(GVFSPlatform.Instance.Constants.PathComparer);
        private Dictionary<string, string> stagedFileDeletes = new Dictionary<string, string>(GVFSPlatform.Instance.Constants.PathComparer);

        // Holds the old-cased paths of directories whose Delete was collapsed into an
        // Add via case-only rename detection. FlushStagedQueues consults this set to
        // suppress child operations (file deletes and child directory Adds) whose
        // parent was case-renamed — those children are carried by the parent rename
        // on disk and do not need separate operations.
        private HashSet<string> directoriesReplacedByCaseRename = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);

        private Enlistment enlistment;
        private GitProcess git;
        private bool diffPerformed;

        public DiffHelper(ITracer tracer, Enlistment enlistment, IEnumerable<string> fileList, IEnumerable<string> folderList, bool includeSymLinks)
            : this(tracer, enlistment, new GitProcess(enlistment), fileList, folderList, includeSymLinks)
        {
        }

        public DiffHelper(ITracer tracer, Enlistment enlistment, GitProcess git, IEnumerable<string> fileList, IEnumerable<string> folderList, bool includeSymLinks)
        {
            this.tracer = tracer;
            this.exactFileList = new HashSet<string>(fileList.Where(x => !x.StartsWith("*")), GVFSPlatform.Instance.Constants.PathComparer);
            this.patternList = fileList.Where(x => x.StartsWith("*")).ToList();
            this.folderList = new List<string>(folderList);
            this.enlistment = enlistment;
            this.git = git;
            this.ShouldIncludeSymLinks = includeSymLinks;

            this.DirectoryOperations = new ConcurrentQueue<DiffTreeResult>();
            this.FileDeleteOperations = new ConcurrentQueue<string>();
            this.FileAddOperations = new ConcurrentDictionary<string, HashSet<PathWithMode>>(GVFSPlatform.Instance.Constants.PathComparer);
            this.RequiredBlobs = new BlockingCollection<string>();
        }

        public bool ShouldIncludeSymLinks { get; set; }

        public bool HasFailures { get; private set; }

        public ConcurrentQueue<DiffTreeResult> DirectoryOperations { get; }

        public ConcurrentQueue<string> FileDeleteOperations { get; }

        /// <summary>
        /// Mapping from available sha to filenames where blob should be written
        /// </summary>
        public ConcurrentDictionary<string, HashSet<PathWithMode>> FileAddOperations { get; }

        /// <summary>
        /// Blobs required to perform a checkout of the destination
        /// </summary>
        public BlockingCollection<string> RequiredBlobs { get; }

        public int TotalDirectoryOperations
        {
            get { return this.stagedDirectoryOperations.Count; }
        }

        public int TotalFileDeletes
        {
            get { return this.stagedFileDeletes.Count; }
        }

        /// <summary>
        /// Returns true if the whole tree was updated
        /// </summary>
        public bool UpdatedWholeTree { get; internal set; } = false;

        public void PerformDiff(string targetCommitSha)
        {
            string targetTreeSha;
            string headTreeSha;
            using (LibGit2Repo repo = new LibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryBackingRoot))
            {
                targetTreeSha = repo.GetTreeSha(targetCommitSha);
                headTreeSha = repo.GetTreeSha("HEAD");
            }

            this.PerformDiff(headTreeSha, targetTreeSha);
        }

        public void PerformDiff(string sourceTreeSha, string targetTreeSha)
        {
            this.EnsureSingleUse();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("TargetTreeSha", targetTreeSha);
            metadata.Add("HeadTreeSha", sourceTreeSha);
            using (ITracer activity = this.tracer.StartActivity("PerformDiff", EventLevel.Informational, Keywords.Telemetry, metadata))
            {
                metadata = new EventMetadata();
                if (sourceTreeSha == null)
                {
                    this.UpdatedWholeTree = true;

                    // Nothing is checked out (fresh git init), so we must search the entire tree.
                    GitProcess.Result result = this.git.LsTree(
                        targetTreeSha,
                        line => this.EnqueueOperationsFromLsTreeLine(activity, line),
                        recursive: true,
                        showAllTrees: true);

                    if (result.ExitCodeIsFailure)
                    {
                        this.HasFailures = true;
                        metadata.Add("Errors", result.Errors);
                        metadata.Add("Output", result.Output.Length > 1024 ? result.Output.Substring(1024) : result.Output);
                    }

                    metadata.Add("Operation", "LsTree");
                }
                else
                {
                    // Diff head and target, determine what needs to be done.
                    GitProcess.Result result = this.git.DiffTree(
                        sourceTreeSha,
                        targetTreeSha,
                        line => this.EnqueueOperationsFromDiffTreeLine(this.tracer, line));

                    if (result.ExitCodeIsFailure)
                    {
                        this.HasFailures = true;
                        metadata.Add("Errors", result.Errors);
                        metadata.Add("Output", result.Output.Length > 1024 ? result.Output.Substring(1024) : result.Output);
                    }

                    metadata.Add("Operation", "DiffTree");
                }

                this.FlushStagedQueues();

                metadata.Add("Success", !this.HasFailures);
                metadata.Add("DirectoryOperationsCount", this.TotalDirectoryOperations);
                metadata.Add("FileDeleteOperationsCount", this.TotalFileDeletes);
                metadata.Add("RequiredBlobsCount", this.RequiredBlobs.Count);
                metadata.Add("FileAddOperationsCount", this.FileAddOperations.Sum(kvp => kvp.Value.Count));
                activity.Stop(metadata);
            }
        }

        public void ParseDiffFile(string filename)
        {
            this.EnsureSingleUse();
            using (ITracer activity = this.tracer.StartActivity("PerformDiff", EventLevel.Informational))
            {
                using (StreamReader file = new StreamReader(File.OpenRead(filename)))
                {
                    while (!file.EndOfStream)
                    {
                        this.EnqueueOperationsFromDiffTreeLine(activity, file.ReadLine());
                    }
                }

                this.FlushStagedQueues();
            }
        }

        private void FlushStagedQueues()
        {
            using (ITracer activity = this.tracer.StartActivity("FlushStagedQueues", EventLevel.Informational))
            {
                HashSet<string> deletedDirectories =
                    new HashSet<string>(
                        this.stagedDirectoryOperations.Values
                        .Where(d => d.Operation == DiffTreeResult.Operations.Delete)
                        .Select(d => d.TargetPath.TrimEnd(Path.DirectorySeparatorChar)),
                        GVFSPlatform.Instance.Constants.PathComparer);

                // Also include directories that were deleted as part of case-only renames.
                // These were replaced by Adds in stagedDirectoryOperations but their children's
                // file deletes should still be filtered out (the parent rename handles them).
                deletedDirectories.UnionWith(this.directoriesReplacedByCaseRename);

                foreach (DiffTreeResult result in this.stagedDirectoryOperations.Values)
                {
                    string parentPath = Path.GetDirectoryName(result.TargetPath.TrimEnd(Path.DirectorySeparatorChar));
                    if (deletedDirectories.Contains(parentPath))
                    {
                        if (result.Operation != DiffTreeResult.Operations.Delete)
                        {
                            // For case renames, child directory Adds inside a case-renamed parent
                            // are expected. The parent rename will handle moving the children.
                            // Only warn if the parent is truly deleted (not case-renamed).
                            if (!this.directoriesReplacedByCaseRename.Contains(parentPath))
                            {
                                EventMetadata metadata = new EventMetadata();
                                metadata.Add(nameof(result.TargetPath), result.TargetPath);
                                metadata.Add(TracingConstants.MessageKey.WarningMessage, "An operation is intended to go inside of a deleted folder");
                                activity.RelatedError("InvalidOperation", metadata);
                            }
                        }
                    }
                    else
                    {
                        this.DirectoryOperations.Enqueue(result);
                    }
                }

                foreach (string filePath in this.stagedFileDeletes.Values)
                {
                    string parentPath = Path.GetDirectoryName(filePath);
                    if (!deletedDirectories.Contains(parentPath))
                    {
                        this.FileDeleteOperations.Enqueue(filePath);
                    }
                }

                this.RequiredBlobs.CompleteAdding();
            }
        }

        private void EnqueueOperationsFromLsTreeLine(ITracer activity, string line)
        {
            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(line);
            if (result == null)
            {
                this.tracer.RelatedError("Unrecognized ls-tree line: {0}", line);
            }

            if (!this.ShouldIncludeResult(result))
            {
                return;
            }

            if (result.TargetIsDirectory)
            {
                if (!this.stagedDirectoryOperations.TryAdd(result.TargetPath, result))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add(nameof(result.TargetPath), result.TargetPath);
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, "File exists in tree with two different cases. Taking the last one.");
                    this.tracer.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                    // Two entries in the same tree differ only in case. Keep the
                    // last one parsed, matching the historical HashSet behavior.
                    this.stagedDirectoryOperations[result.TargetPath] = result;
                }
            }
            else
            {
                this.EnqueueFileAddOperation(activity, result);
            }
        }

        private void EnqueueOperationsFromDiffTreeLine(ITracer activity, string line)
        {
            if (!line.StartsWith(":"))
            {
                // Diff-tree starts with metadata we can ignore.
                // Real diff lines always start with a colon
                return;
            }

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(line);
            if (!this.ShouldIncludeResult(result))
            {
                return;
            }

            if (result.Operation == DiffTreeResult.Operations.Unknown ||
                result.Operation == DiffTreeResult.Operations.Unmerged ||
                result.Operation == DiffTreeResult.Operations.CopyEdit ||
                result.Operation == DiffTreeResult.Operations.RenameEdit)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(result.TargetPath), result.TargetPath);
                metadata.Add(nameof(line), line);
                activity.RelatedError(metadata, "Unexpected diff operation: " + result.Operation);
                this.HasFailures = true;
                return;
            }

            // Separate and enqueue all directory operations first.
            if (result.SourceIsDirectory || result.TargetIsDirectory)
            {
                switch (result.Operation)
                {
                    case DiffTreeResult.Operations.Delete:
                        if (!this.stagedDirectoryOperations.TryAdd(result.TargetPath, result))
                        {
                            // A directory with the same (case-insensitive) path was already
                            // staged as an Add. This is a case-only rename where diff-tree
                            // emitted the Add before the Delete. Either emit order is possible
                            // because git diff-tree compares tree entries by byte order, so
                            // whichever casing sorts lower appears first.
                            //
                            // Annotate the staged Add with the old-cased path so CheckoutStage
                            // can perform the rename. Keep the Add — never the Delete — to
                            // avoid deleting a folder out from under ourselves.
                            DiffTreeResult existingOp = this.stagedDirectoryOperations[result.TargetPath];
                            if (!existingOp.TargetPath.Equals(result.TargetPath, StringComparison.Ordinal))
                            {
                                existingOp.SourcePath = result.TargetPath;
                                this.directoriesReplacedByCaseRename.Add(result.TargetPath.TrimEnd(Path.DirectorySeparatorChar));
                                TraceCaseRename(activity, "Directory", oldPath: result.TargetPath, newPath: existingOp.TargetPath);
                            }
                            else
                            {
                                TraceDuplicate(activity, "Directory", "Delete", result.TargetPath);
                            }
                        }

                        break;
                    case DiffTreeResult.Operations.Add:
                    case DiffTreeResult.Operations.Modify:
                        if (!this.stagedDirectoryOperations.TryAdd(result.TargetPath, result))
                        {
                            // A directory with the same path (case-insensitive) was already staged.
                            // This is a case-only rename: the Delete was staged first, now the Add arrives.
                            DiffTreeResult existingOp = this.stagedDirectoryOperations[result.TargetPath];
                            if (!existingOp.TargetPath.Equals(result.TargetPath, StringComparison.Ordinal))
                            {
                                // Case-only rename: store the old-cased path so CheckoutStage can rename the directory
                                result.SourcePath = existingOp.TargetPath;
                                this.directoriesReplacedByCaseRename.Add(existingOp.TargetPath.TrimEnd(Path.DirectorySeparatorChar));
                                TraceCaseRename(activity, "Directory", oldPath: existingOp.TargetPath, newPath: result.TargetPath);
                            }
                            else
                            {
                                TraceDuplicate(activity, "Directory", result.Operation.ToString(), result.TargetPath);
                            }

                            // Replace the delete with the add to make sure we don't delete a folder from under ourselves
                            this.stagedDirectoryOperations[result.TargetPath] = result;
                        }

                        break;
                    default:
                        activity.RelatedError("Unexpected diff operation from line: {0}", line);
                        break;
                }
            }
            else
            {
                switch (result.Operation)
                {
                    case DiffTreeResult.Operations.Delete:
                        this.EnqueueFileDeleteOperation(activity, result.TargetPath);

                        break;
                    case DiffTreeResult.Operations.Modify:
                    case DiffTreeResult.Operations.Add:
                        this.EnqueueFileAddOperation(activity, result);
                        break;
                    default:
                        activity.RelatedError("Unexpected diff operation from line: {0}", line);
                        break;
                }
            }
        }

        private bool ShouldIncludeResult(DiffTreeResult blobAdd)
        {
            if (blobAdd.TargetIsSymLink && !this.ShouldIncludeSymLinks)
            {
                return false;
            }

            if (blobAdd.TargetPath == null)
            {
                return true;
            }

            if (this.exactFileList.Count == 0 &&
                this.patternList.Count == 0 &&
                this.folderList.Count == 0)
            {
                return true;
            }

            if (this.exactFileList.Contains(blobAdd.TargetPath) ||
                this.patternList.Any(path => blobAdd.TargetPath.EndsWith(path.Substring(1), GVFSPlatform.Instance.Constants.PathComparison)))
            {
                return true;
            }

            if (this.folderList.Any(path => blobAdd.TargetPath.StartsWith(path, GVFSPlatform.Instance.Constants.PathComparison)))
            {
                return true;
            }

            return false;
        }

        private void EnqueueFileDeleteOperation(ITracer activity, string targetPath)
        {
            // Use case-sensitive check: if the exact same path (same casing) was already added,
            // this is a true duplicate, not a case rename. Skip it.
            // But if it matches case-insensitively only, this is a case rename — allow the delete through
            // so the old-cased file is removed before the new-cased file is written.
            if (this.filesAdded.TryGetValue(targetPath, out string existingAddedPath))
            {
                if (existingAddedPath.Equals(targetPath, StringComparison.Ordinal))
                {
                    TraceDuplicate(activity, "File", "Delete", targetPath);
                    return;
                }

                TraceCaseRename(activity, "File", oldPath: targetPath, newPath: existingAddedPath);
            }

            // Either no prior add, or a case-only difference: allow the delete to be
            // staged so the old casing is removed from disk before the new add lands.
            this.stagedFileDeletes.TryAdd(targetPath, targetPath);
        }

        /// <remarks>
        /// This is not used in a multithreaded method, it doesn't need to be thread-safe
        /// </remarks>
        private void EnqueueFileAddOperation(ITracer activity, DiffTreeResult operation)
        {
            // Each filepath should be unique according to GVFSPlatform.Instance.Constants.PathComparer.
            // If there are duplicates, only the last parsed one should remain.
            if (!this.filesAdded.TryAdd(operation.TargetPath, operation.TargetPath))
            {
                foreach (KeyValuePair<string, HashSet<PathWithMode>> kvp in this.FileAddOperations)
                {
                    PathWithMode tempPathWithMode = new PathWithMode(operation.TargetPath, 0x0000);
                    if (kvp.Value.Remove(tempPathWithMode))
                    {
                        break;
                    }
                }
            }

            // If a delete is already staged for the same path under the case-insensitive
            // comparer, decide whether this is a true duplicate (same casing → drop the
            // delete) or a case-only rename (different casing → keep the delete so the
            // old casing is removed from disk before the new add lands).
            if (this.stagedFileDeletes.TryGetValue(operation.TargetPath, out string existingDeletePath))
            {
                if (existingDeletePath.Equals(operation.TargetPath, StringComparison.Ordinal))
                {
                    TraceDuplicate(activity, "File", "Add", operation.TargetPath);
                    this.stagedFileDeletes.Remove(operation.TargetPath);
                }
                else
                {
                    TraceCaseRename(activity, "File", oldPath: existingDeletePath, newPath: operation.TargetPath);
                }
            }

            this.FileAddOperations.AddOrUpdate(
                operation.TargetSha,
                new HashSet<PathWithMode> { new PathWithMode(operation.TargetPath, operation.TargetMode) },
                (key, oldValue) =>
                {
                    oldValue.Add(new PathWithMode(operation.TargetPath, operation.TargetMode));
                    return oldValue;
                });

            this.RequiredBlobs.Add(operation.TargetSha);
        }

        private static void TraceCaseRename(ITracer activity, string kind, string oldPath, string newPath)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Kind", kind);
            metadata.Add("OldPath", oldPath);
            metadata.Add("NewPath", newPath);
            activity.RelatedEvent(EventLevel.Informational, "CaseRename", metadata);
        }

        private static void TraceDuplicate(ITracer activity, string kind, string operation, string targetPath)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Kind", kind);
            metadata.Add("Operation", operation);
            metadata.Add(nameof(targetPath), targetPath);
            metadata.Add(TracingConstants.MessageKey.WarningMessage, "Duplicate diff entry for the same path; later occurrence collapsed into earlier.");
            activity.RelatedEvent(EventLevel.Warning, "DuplicateDiffEntry", metadata);
        }

        private void EnsureSingleUse()
        {
            // The output collections — DirectoryOperations, FileDeleteOperations,
            // FileAddOperations, RequiredBlobs — are populated incrementally and
            // RequiredBlobs.CompleteAdding() is called at the end of FlushStagedQueues.
            // A second call would attempt to add to a completed BlockingCollection
            // and throw deep in the parsing path, leaving partial output. The class
            // is therefore intended to be single-use; instantiate a new DiffHelper
            // for each diff.
            if (this.diffPerformed)
            {
                throw new InvalidOperationException(
                    $"{nameof(DiffHelper)} has already produced a diff and cannot be reused. Construct a new instance.");
            }

            this.diffPerformed = true;
        }

    }
}
