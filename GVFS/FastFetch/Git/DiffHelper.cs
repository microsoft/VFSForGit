using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Git
{
    public class DiffHelper
    {
        private const string AreaPath = nameof(DiffHelper);

        private ITracer tracer;
        private List<string> pathWhitelist;
        private List<string> deletedPaths = new List<string>();
        private HashSet<string> filesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Enlistment enlistment;
        private string targetCommitSha;

        private int additionalDirDeletes = 0;
        private int additionalFileDeletes = 0;

        public DiffHelper(ITracer tracer, Enlistment enlistment, string targetCommitSha, IEnumerable<string> pathWhitelist)
        {
            this.tracer = tracer;
            this.pathWhitelist = new List<string>(pathWhitelist);
            this.enlistment = enlistment;
            this.targetCommitSha = targetCommitSha;

            this.DirectoryOperations = new ConcurrentQueue<Git.DiffTreeResult>();
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
            get { return this.DirectoryOperations.Count + this.additionalDirDeletes; }
        }

        public int TotalFileDeletes
        {
            get { return this.FileDeleteOperations.Count + this.additionalFileDeletes; }
        }

        public void PerformDiff()
        {
            using (GitCatFileBatchProcess catFile = new GitCatFileBatchProcess(this.enlistment))
            {
                GitProcess git = new GitProcess(this.enlistment);
                string repoRoot = git.GetRepoRoot();

                string targetTreeSha = catFile.GetTreeSha(this.targetCommitSha);
                string headTreeSha = catFile.GetTreeSha("HEAD");

                EventMetadata metadata = new EventMetadata();
                metadata.Add("TargetTreeSha", targetTreeSha);
                metadata.Add("HeadTreeSha", headTreeSha);
                using (ITracer activity = this.tracer.StartActivity("PerformDiff", EventLevel.Informational, metadata))
                {
                    metadata = new EventMetadata();
                    if (headTreeSha == null)
                    {
                        // Nothing is checked out (fresh git init), so we must search the entire tree.
                        git.LsTree(targetTreeSha, this.EnqueueOperationsFromLsTreeLine, recursive: true, showAllTrees: true);
                        metadata.Add("Operation", "LsTree");
                    }
                    else
                    {
                        // Diff head and target, determine what needs to be done.
                        git.DiffTree(headTreeSha, targetTreeSha, line => this.EnqueueOperationsFromDiffTreeLine(this.tracer, repoRoot, line));
                        metadata.Add("Operation", "DiffTree");
                    }

                    this.RequiredBlobs.CompleteAdding();

                    metadata.Add("Success", !this.HasFailures);
                    metadata.Add("DirectoryOperationsCount", this.TotalDirectoryOperations);
                    metadata.Add("FileDeleteOperationsCount", this.TotalFileDeletes);
                    metadata.Add("RequiredBlobsCount", this.RequiredBlobs.Count);
                    activity.Stop(metadata);
                }
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
            }
        }
        
        private void EnqueueOperationsFromLsTreeLine(string line)
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
                this.DirectoryOperations.Enqueue(result);
            }
            else
            {
                this.EnqueueFileAddOperation(result);
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

            if (result.Operation == DiffTreeResult.Operations.Delete)
            {
                // Don't enqueue deletes that will be handled by recursively deleting their parent.
                // Git traverses diffs in pre-order, so we are guaranteed to ignore child deletes here.
                // Append trailing slash terminator to avoid matches with directory prefixes (Eg. \GVFS and \GVFS.Common)
                string pathWithSlash = result.TargetFilename + "\\";
                if (this.deletedPaths.Any(path => pathWithSlash.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                {
                    if (result.SourceIsDirectory || result.TargetIsDirectory)
                    {
                        Interlocked.Increment(ref this.additionalDirDeletes);
                    }
                    else
                    {
                        Interlocked.Increment(ref this.additionalFileDeletes);
                    }

                    return;
                }

                this.deletedPaths.Add(pathWithSlash);
            }

            // Separate and enqueue all directory operations first.
            if (result.SourceIsDirectory || result.TargetIsDirectory)
            {
                // Handle when a directory becomes a file.
                // Files becoming directories is handled by HandleAllDirectoryOperations
                if (result.Operation == DiffTreeResult.Operations.RenameEdit &&
                    !result.TargetIsDirectory)
                {
                    this.EnqueueFileAddOperation(result);
                }

                this.DirectoryOperations.Enqueue(result);
            }
            else
            {
                switch (result.Operation)
                {
                    case DiffTreeResult.Operations.Delete:
                        this.FileDeleteOperations.Enqueue(result.TargetFilename);
                        break;
                    case DiffTreeResult.Operations.RenameEdit:
                        this.FileDeleteOperations.Enqueue(result.SourceFilename);
                        this.EnqueueFileAddOperation(result);
                        break;
                    case DiffTreeResult.Operations.Modify:
                    case DiffTreeResult.Operations.CopyEdit:
                    case DiffTreeResult.Operations.Add:
                        this.EnqueueFileAddOperation(result);
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
                !this.pathWhitelist.Any() ||
                this.pathWhitelist.Any(path => blobAdd.TargetFilename.StartsWith(path, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <remarks>
        /// This is not used in a multithreaded method, it doesn't need to be thread-safe
        /// </remarks>
        private void EnqueueFileAddOperation(DiffTreeResult operation)
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
            
            HashSet<string> operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { operation.TargetFilename };
            this.FileAddOperations.AddOrUpdate(
                operation.TargetSha,
                operations,
                (key, oldValue) =>
                {
                    oldValue.Add(operation.TargetFilename);
                    return oldValue;
                });

            this.RequiredBlobs.Add(operation.TargetSha);
        }
    }
}
