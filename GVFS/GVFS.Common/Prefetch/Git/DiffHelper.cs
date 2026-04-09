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
        private HashSet<string> filesAdded = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);

        private HashSet<DiffTreeResult> stagedDirectoryOperations = new HashSet<DiffTreeResult>(new DiffTreeByNameComparer());
        private HashSet<string> stagedFileDeletes = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);

        private Enlistment enlistment;
        private GitProcess git;

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
                        this.stagedDirectoryOperations
                        .Where(d => d.Operation == DiffTreeResult.Operations.Delete)
                        .Select(d => d.TargetPath.TrimEnd(Path.DirectorySeparatorChar)),
                        GVFSPlatform.Instance.Constants.PathComparer);

                foreach (DiffTreeResult result in this.stagedDirectoryOperations)
                {
                    string parentPath = Path.GetDirectoryName(result.TargetPath.TrimEnd(Path.DirectorySeparatorChar));
                    if (deletedDirectories.Contains(parentPath))
                    {
                        if (result.Operation != DiffTreeResult.Operations.Delete)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add(nameof(result.TargetPath), result.TargetPath);
                            metadata.Add(TracingConstants.MessageKey.WarningMessage, "An operation is intended to go inside of a deleted folder");
                            activity.RelatedError("InvalidOperation", metadata);
                        }
                    }
                    else
                    {
                        this.DirectoryOperations.Enqueue(result);
                    }
                }

                foreach (string filePath in this.stagedFileDeletes)
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
                if (!this.stagedDirectoryOperations.Add(result))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add(nameof(result.TargetPath), result.TargetPath);
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, "File exists in tree with two different cases. Taking the last one.");
                    this.tracer.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                    // Since we match only on filename, re-adding is the easiest way to update the set.
                    this.stagedDirectoryOperations.Remove(result);
                    this.stagedDirectoryOperations.Add(result);
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
                        if (!this.stagedDirectoryOperations.Add(result))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add(nameof(result.TargetPath), result.TargetPath);
                            metadata.Add(TracingConstants.MessageKey.WarningMessage, "A case change was attempted. It will not be reflected in the working directory.");
                            activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);
                        }

                        break;
                    case DiffTreeResult.Operations.Add:
                    case DiffTreeResult.Operations.Modify:
                        if (!this.stagedDirectoryOperations.Add(result))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add(nameof(result.TargetPath), result.TargetPath);
                            metadata.Add(TracingConstants.MessageKey.WarningMessage, "A case change was attempted. It will not be reflected in the working directory.");
                            activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                            // Replace the delete with the add to make sure we don't delete a folder from under ourselves
                            this.stagedDirectoryOperations.Remove(result);
                            this.stagedDirectoryOperations.Add(result);
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
            if (this.filesAdded.Contains(targetPath))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(targetPath), targetPath);
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "A case change was attempted. It will not be reflected in the working directory.");
                activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                return;
            }

            this.stagedFileDeletes.Add(targetPath);
        }

        /// <remarks>
        /// This is not used in a multithreaded method, it doesn't need to be thread-safe
        /// </remarks>
        private void EnqueueFileAddOperation(ITracer activity, DiffTreeResult operation)
        {
            // Each filepath should be unique according to GVFSPlatform.Instance.Constants.PathComparer.
            // If there are duplicates, only the last parsed one should remain.
            if (!this.filesAdded.Add(operation.TargetPath))
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

            if (this.stagedFileDeletes.Remove(operation.TargetPath))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(operation.TargetPath), operation.TargetPath);
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "A case change was attempted. It will not be reflected in the working directory.");
                activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);
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

        private class DiffTreeByNameComparer : IEqualityComparer<DiffTreeResult>
        {
            public bool Equals(DiffTreeResult x, DiffTreeResult y)
            {
                if (x.TargetPath != null)
                {
                    if (y.TargetPath != null)
                    {
                        return x.TargetPath.Equals(y.TargetPath, GVFSPlatform.Instance.Constants.PathComparison);
                    }

                    return false;
                }
                else
                {
                    // both null means they're equal
                    return y.TargetPath == null;
                }
            }

            public int GetHashCode(DiffTreeResult obj)
            {
                return obj.TargetPath != null ?
                    GVFSPlatform.Instance.Constants.PathComparer.GetHashCode(obj.TargetPath) : 0;
            }
        }
    }
}
