using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Prefetch.Pipeline;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FastFetch
{
    public class CheckoutStage : PrefetchPipelineStage
    {
        private const string AreaPath = nameof(CheckoutStage);
        private const int NumOperationsPerStatus = 10000;

        private ITracer tracer;
        private Enlistment enlistment;
        private PhysicalFileSystem fileSystem;
        private string targetCommitSha;
        private bool forceCheckout;

        private DiffHelper diff;

        private int directoryOpCount = 0;
        private int fileDeleteCount = 0;
        private int fileWriteCount = 0;
        private long bytesWritten = 0;
        private long shasReceived = 0;

        // Checkout requires synchronization between the delete/directory/add stages, so control the parallelization
        private int maxParallel;

        public CheckoutStage(int maxParallel, IEnumerable<string> folderList, string targetCommitSha, ITracer tracer, Enlistment enlistment, bool forceCheckout)
            : base(maxParallel: 1)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);
            this.enlistment = enlistment;
            this.fileSystem = new PhysicalFileSystem();
            this.diff = new DiffHelper(tracer, enlistment, new string[0], folderList, includeSymLinks: true);
            this.targetCommitSha = targetCommitSha;
            this.forceCheckout = forceCheckout;
            this.AvailableBlobShas = new BlockingCollection<string>();

            // Keep track of how parallel we're expected to be later during DoWork
            // Note that '1' is passed to the base object, forcing DoWork to be single threaded
            // This allows us to control the synchronization between stages by doing the parallization ourselves
            this.maxParallel = maxParallel;
        }

        public BlockingCollection<string> RequiredBlobs
        {
            get { return this.diff.RequiredBlobs; }
        }

        public BlockingCollection<string> AvailableBlobShas { get; }

        public bool UpdatedWholeTree
        {
            get { return this.diff.UpdatedWholeTree; }
        }

        public BlockingCollection<string> AddedOrEditedLocalFiles { get; } = new BlockingCollection<string>();

        protected override void DoBeforeWork()
        {
            if (this.forceCheckout)
            {
                // Force search the entire tree by treating the repo as if it were brand new.
                this.diff.PerformDiff(sourceTreeSha: null, targetTreeSha: this.targetCommitSha);
            }
            else
            {
                // Let the diff find the sourceTreeSha on its own.
                this.diff.PerformDiff(this.targetCommitSha);
            }

            this.HasFailures = this.diff.HasFailures;
        }

        protected override void DoWork()
        {
            // Do the delete operations first as they can't have dependencies on other work
            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllFileDeleteOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                Parallel.For(1, this.maxParallel, (i) => { this.HandleAllFileDeleteOperations(); });
                EventMetadata metadata = new EventMetadata();
                metadata.Add("FilesDeleted", this.fileDeleteCount);
                activity.Stop(metadata);
            }

            // Do directory operations after deletes in case a file delete must be done first
            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllDirectoryOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                Parallel.For(1, this.maxParallel, (i) => { this.HandleAllDirectoryOperations(); });
                EventMetadata metadata = new EventMetadata();
                metadata.Add("DirectoryOperationsCompleted", this.directoryOpCount);
                activity.Stop(metadata);
            }

            // Do add operations last, after all deletes and directories have been created
            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllFileAddOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                Parallel.For(1, this.maxParallel, (i) => { this.HandleAllFileAddOperations(); });
                EventMetadata metadata = new EventMetadata();
                metadata.Add("FilesWritten", this.fileWriteCount);
                activity.Stop(metadata);
            }
        }

        protected override void DoAfterWork()
        {
            // If for some reason a blob doesn't become available,
            // checkout might complete with file writes still left undone.
            if (this.diff.FileAddOperations.Count > 0)
            {
                this.HasFailures = true;
                EventMetadata errorMetadata = new EventMetadata();
                if (this.diff.FileAddOperations.Count < 10)
                {
                    errorMetadata.Add("RemainingShas", string.Join(",", this.diff.FileAddOperations.Keys));
                }
                else
                {
                    errorMetadata.Add("RemainingShaCount", this.diff.FileAddOperations.Count);
                }

                this.tracer.RelatedError(errorMetadata, "Not all file writes were completed");
            }

            this.AddedOrEditedLocalFiles.CompleteAdding();

            EventMetadata metadata = new EventMetadata();
            metadata.Add("DirectoryOperations", this.directoryOpCount);
            metadata.Add("FileDeletes", this.fileDeleteCount);
            metadata.Add("FileWrites", this.fileWriteCount);
            metadata.Add("BytesWritten", this.bytesWritten);
            metadata.Add("ShasReceived", this.shasReceived);
            this.tracer.Stop(metadata);
        }

        private void HandleAllDirectoryOperations()
        {
            DiffTreeResult treeOp;
            while (this.diff.DirectoryOperations.TryDequeue(out treeOp))
            {
                if (this.HasFailures)
                {
                    return;
                }

                switch (treeOp.Operation)
                {
                    case DiffTreeResult.Operations.Modify:
                    case DiffTreeResult.Operations.Add:
                        try
                        {
                            Directory.CreateDirectory(treeOp.TargetPath);
                        }
                        catch (Exception ex)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Operation", "CreateDirectory");
                            metadata.Add(nameof(treeOp.TargetPath), treeOp.TargetPath);
                            this.tracer.RelatedError(metadata, ex.Message);
                            this.HasFailures = true;
                        }

                        break;
                    case DiffTreeResult.Operations.Delete:
                        try
                        {
                            if (Directory.Exists(treeOp.TargetPath))
                            {
                                this.fileSystem.RecursiveDelete(treeOp.TargetPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            // We are deleting directories and subdirectories in parallel
                            if (Directory.Exists(treeOp.TargetPath))
                            {
                                EventMetadata metadata = new EventMetadata();
                                metadata.Add("Operation", "DeleteDirectory");
                                metadata.Add(nameof(treeOp.TargetPath), treeOp.TargetPath);
                                this.tracer.RelatedError(metadata, ex.Message);
                                this.HasFailures = true;
                            }
                        }

                        break;
                    default:
                        this.tracer.RelatedError("Ignoring unexpected Tree Operation {0}: {1}", treeOp.TargetPath, treeOp.Operation);
                        continue;
                }

                if (Interlocked.Increment(ref this.directoryOpCount) % NumOperationsPerStatus == 0)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("DirectoryOperationsQueued", this.diff.DirectoryOperations.Count);
                    metadata.Add("DirectoryOperationsCompleted", this.directoryOpCount);
                    this.tracer.RelatedEvent(EventLevel.Informational, "CheckoutStatus", metadata);
                }
            }
        }

        private void HandleAllFileDeleteOperations()
        {
            string path;
            while (this.diff.FileDeleteOperations.TryDequeue(out path))
            {
                if (this.HasFailures)
                {
                    return;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    Interlocked.Increment(ref this.fileDeleteCount);
                }
                catch (Exception ex)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Operation", "DeleteFile");
                    metadata.Add("Path", path);
                    this.tracer.RelatedError(metadata, ex.Message);
                    this.HasFailures = true;
                }
            }
        }

        private void HandleAllFileAddOperations()
        {
            using (FastFetchLibGit2Repo repo = new FastFetchLibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryRoot))
            {
                string availableBlob;
                while (this.AvailableBlobShas.TryTake(out availableBlob, Timeout.Infinite))
                {
                    if (this.HasFailures)
                    {
                        return;
                    }

                    Interlocked.Increment(ref this.shasReceived);

                    HashSet<PathWithMode> paths;
                    if (this.diff.FileAddOperations.TryRemove(availableBlob, out paths))
                    {
                        try
                        {
                            long written;
                            if (!repo.TryCopyBlobToFile(availableBlob, paths, out written))
                            {
                                // TryCopyBlobTo emits an error event.
                                this.HasFailures = true;
                            }

                            Interlocked.Add(ref this.bytesWritten, written);

                            foreach (PathWithMode modeAndPath in paths)
                            {
                                this.AddedOrEditedLocalFiles.Add(modeAndPath.Path);

                                if (Interlocked.Increment(ref this.fileWriteCount) % NumOperationsPerStatus == 0)
                                {
                                    EventMetadata metadata = new EventMetadata();
                                    metadata.Add("AvailableBlobsQueued", this.AvailableBlobShas.Count);
                                    metadata.Add("NumberBlobsNeeded", this.diff.FileAddOperations.Count);
                                    this.tracer.RelatedEvent(EventLevel.Informational, "CheckoutStatus", metadata);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            EventMetadata errorData = new EventMetadata();
                            errorData.Add("Operation", "WriteFile");
                            this.tracer.RelatedError(errorData, ex.ToString());
                            this.HasFailures = true;
                        }
                    }
                }
            }
        }
    }
}