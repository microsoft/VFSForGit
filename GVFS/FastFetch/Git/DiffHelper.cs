using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Git
{
    public class DiffHelper
    {
        private const string AreaPath = nameof(DiffHelper);

        private ITracer tracer;
        private List<string> pathWhitelist;
        private HashSet<string> filesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<DiffTreeResult> stagedDirectoryOperations = new HashSet<DiffTreeResult>(new DiffTreeByNameComparer());
        private HashSet<string> stagedFileDeletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Enlistment enlistment;
        private GitProcess git;

        public DiffHelper(ITracer tracer, Enlistment enlistment, IEnumerable<string> pathWhitelist)
            : this(tracer, enlistment, new GitProcess(enlistment), pathWhitelist)
        {
        }

        public DiffHelper(ITracer tracer, Enlistment enlistment, GitProcess git, IEnumerable<string> pathWhitelist)
        {
            this.tracer = tracer;
            this.pathWhitelist = new List<string>(pathWhitelist);
            this.enlistment = enlistment;
            this.git = git;

            this.DirectoryOperations = new ConcurrentQueue<DiffTreeResult>();
            this.FileDeleteOperations = new ConcurrentQueue<string>();
            this.FileAddOperations = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            this.RequiredBlobs = new BlockingCollection<string>();
        }

        public bool HasFailures { get; private set; }

        public ConcurrentQueue<DiffTreeResult> DirectoryOperations { get; }

        public ConcurrentQueue<string> FileDeleteOperations { get; }

        /// <summary>
        /// Mapping from available sha to filenames where blob should be written
        /// </summary>
        public ConcurrentDictionary<string, HashSet<string>> FileAddOperations { get; }

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
            using (LibGit2Repo repo = new LibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryRoot))
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

                    if (result.HasErrors)
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
                        line => this.EnqueueOperationsFromDiffTreeLine(this.tracer, this.enlistment.EnlistmentRoot, line));
                    
                    if (result.HasErrors)
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
                activity.Stop(metadata);
            }
        }

        public void ParseDiffFile(string filename, string repoRoot)
        {
            using (ITracer activity = this.tracer.StartActivity("PerformDiff", EventLevel.Informational))
            {
                using (StreamReader file = new StreamReader(File.OpenRead(filename)))
                {
                    while (!file.EndOfStream)
                    {
                        this.EnqueueOperationsFromDiffTreeLine(activity, repoRoot, file.ReadLine());
                    }
                }

                this.FlushStagedQueues();
            }
        }

        private void FlushStagedQueues()
        {
            List<string> deletedPaths = new List<string>();
            foreach (DiffTreeResult result in this.stagedDirectoryOperations)
            {
                // Don't enqueue deletes that will be handled by recursively deleting their parent.
                // Git traverses diffs in pre-order, so we are guaranteed to ignore child deletes here.
                // Append trailing slash terminator to avoid matches with directory prefixes (Eg. \GVFS and \GVFS.Common)
                if (result.Operation == DiffTreeResult.Operations.Delete)
                {
                    string pathWithSlash = result.TargetFilename + "\\";
                    if (deletedPaths.Any(path => pathWithSlash.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    deletedPaths.Add(pathWithSlash);
                }

                this.DirectoryOperations.Enqueue(result);
            }

            foreach (string filePath in this.stagedFileDeletes)
            {
                string pathWithSlash = filePath + "\\";
                if (deletedPaths.Any(path => pathWithSlash.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                deletedPaths.Add(pathWithSlash);

                this.FileDeleteOperations.Enqueue(filePath);
            }

            this.RequiredBlobs.CompleteAdding();
        }

        private void EnqueueOperationsFromLsTreeLine(ITracer activity, string line)
        {
            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(line, this.enlistment.EnlistmentRoot);
            if (result == null)
            {
                this.tracer.RelatedError("Unrecognized ls-tree line: {0}", line);
            }

            if (!this.ResultIsInWhitelist(result))
            {
                return;
            }

            if (result.TargetIsDirectory)
            {
                if (!this.stagedDirectoryOperations.Add(result))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Filename", result.TargetFilename);
                    metadata.Add("Message", "File exists in tree with two different cases. Taking the last one.");
                    this.tracer.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                    // Since we match only on filename, readding is the easiest way to update the set.
                    this.stagedDirectoryOperations.Remove(result);
                    this.stagedDirectoryOperations.Add(result);
                }
            }
            else
            {
                this.EnqueueFileAddOperation(activity, result);
            }
        }

        private void EnqueueOperationsFromDiffTreeLine(ITracer activity, string repoRoot, string line)
        {
            if (!line.StartsWith(":"))
            {
                // Diff-tree starts with metadata we can ignore.
                // Real diff lines always start with a colon
                return;
            }

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(line, repoRoot);
            if (!this.ResultIsInWhitelist(result))
            {
                return;
            }

            if (result.Operation == DiffTreeResult.Operations.Unknown ||
                result.Operation == DiffTreeResult.Operations.Unmerged)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Path", result.TargetFilename);
                metadata.Add("ErrorMessage", "Unexpected diff operation: " + result.Operation);
                activity.RelatedError(metadata);
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
                            metadata.Add("Filename", result.TargetFilename);
                            metadata.Add("Message", "A case change was attempted. It will not be reflected in the working directory.");
                            activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);
                        }

                        break;
                    case DiffTreeResult.Operations.RenameEdit:
                        if (!this.stagedDirectoryOperations.Add(result))
                        {
                            // This could happen if a directory was deleted and an existing directory was renamed to replace it, but with a different case.
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Filename", result.TargetFilename);
                            metadata.Add("Message", "A case change was attempted. It will not be reflected in the working directory.");
                            activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);

                            // The target of RenameEdit is always akin to an Add, so replacing the delete is the safer thing to do.
                            this.stagedDirectoryOperations.Remove(result);
                            this.stagedDirectoryOperations.Add(result);
                        }

                        if (!result.TargetIsDirectory)
                        {
                            // Handle when a directory becomes a file.
                            // Files becoming directories is handled by HandleAllDirectoryOperations
                            this.EnqueueFileAddOperation(activity, result);
                        }

                        break;
                    case DiffTreeResult.Operations.Add:
                    case DiffTreeResult.Operations.Modify:
                    case DiffTreeResult.Operations.CopyEdit:
                        if (!this.stagedDirectoryOperations.Add(result))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Filename", result.TargetFilename);
                            metadata.Add("Message", "A case change was attempted. It will not be reflected in the working directory.");
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
                        this.EnqueueFileDeleteOperation(activity, result.TargetFilename);

                        break;
                    case DiffTreeResult.Operations.RenameEdit:

                        this.EnqueueFileAddOperation(activity, result);
                        this.EnqueueFileDeleteOperation(activity, result.SourceFilename);

                        break;
                    case DiffTreeResult.Operations.Modify:
                    case DiffTreeResult.Operations.CopyEdit:
                    case DiffTreeResult.Operations.Add:
                        this.EnqueueFileAddOperation(activity, result);
                        break;
                    default:
                        activity.RelatedError("Unexpected diff operation from line: {0}", line);
                        break;
                }
            }
        }

        private bool ResultIsInWhitelist(DiffTreeResult blobAdd)
        {
            return blobAdd.TargetFilename == null ||
                this.pathWhitelist.Count == 0 ||
                this.pathWhitelist.Any(path => blobAdd.TargetFilename.StartsWith(path, StringComparison.OrdinalIgnoreCase));
        }

        private void EnqueueFileDeleteOperation(ITracer activity, string targetPath)
        {
            if (this.filesAdded.Contains(targetPath))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Filename", targetPath);
                metadata.Add("Message", "A case change was attempted. It will not be reflected in the working directory.");
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
            // Each filepath should be case-insensitive unique. If there are duplicates, only the last parsed one should remain.
            if (!this.filesAdded.Add(operation.TargetFilename))
            {
                foreach (KeyValuePair<string, HashSet<string>> kvp in this.FileAddOperations)
                {
                    if (kvp.Value.Remove(operation.TargetFilename))
                    {
                        break;
                    }
                }
            }

            if (this.stagedFileDeletes.Remove(operation.TargetFilename))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Filename", operation.TargetFilename);
                metadata.Add("Message", "A case change was attempted. It will not be reflected in the working directory.");
                activity.RelatedEvent(EventLevel.Warning, "CaseConflict", metadata);
            }

            this.FileAddOperations.AddOrUpdate(
                operation.TargetSha,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { operation.TargetFilename },
                (key, oldValue) =>
                {
                    oldValue.Add(operation.TargetFilename);
                    return oldValue;
                });

            this.RequiredBlobs.Add(operation.TargetSha);
        }

        private class DiffTreeByNameComparer : IEqualityComparer<DiffTreeResult>
        {
            public bool Equals(DiffTreeResult x, DiffTreeResult y)
            {
                if (x.TargetFilename != null)
                {
                    if (y.TargetFilename != null)
                    {
                        return x.TargetFilename.Equals(y.TargetFilename, StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                }
                else
                {
                    // both null means they're equal
                    return y.TargetFilename == null;
                }
            }

            public int GetHashCode(DiffTreeResult obj)
            {
                return obj.TargetFilename != null ?
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetFilename) : 0;
            }
        }
    }
}
