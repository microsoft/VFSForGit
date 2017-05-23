using FastFetch.Git;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FastFetch.Jobs
{
    public class CheckoutJob : Job
    {
        private const string AreaPath = nameof(CheckoutJob);
        private const int NumOperationsPerStatus = 10000;
        
        private ITracer tracer;
        private Enlistment enlistment;
        private string targetCommitSha;

        private DiffHelper diff;

        private int directoryOpCount = 0;
        private int fileDeleteCount = 0;
        private int fileWriteCount = 0;
        private long bytesWritten = 0;
        private long shasReceived = 0;

        public CheckoutJob(int maxParallel, IEnumerable<string> pathWhitelist, string targetCommitSha, ITracer tracer, Enlistment enlistment)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);
            this.enlistment = enlistment;
            this.diff = new DiffHelper(tracer, enlistment, pathWhitelist);
            this.targetCommitSha = targetCommitSha;
            this.AvailableBlobShas = new BlockingCollection<string>();
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
            this.diff.PerformDiff(this.targetCommitSha);
            this.HasFailures = this.diff.HasFailures;
        }

        protected override void DoWork()
        {
            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllDirectoryOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                this.HandleAllDirectoryOperations();

                EventMetadata metadata = new EventMetadata();
                metadata.Add("DirectoryOperationsCompleted", this.directoryOpCount);
                activity.Stop(metadata);
            }

            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllFileDeleteOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                this.HandleAllFileDeleteOperations();

                EventMetadata metadata = new EventMetadata();
                metadata.Add("FilesDeleted", this.fileDeleteCount);
                activity.Stop(metadata);
            }

            using (ITracer activity = this.tracer.StartActivity(
                nameof(this.HandleAllFileAddOperations),
                EventLevel.Informational,
                Keywords.Telemetry,
                metadata: null))
            {
                this.HandleAllFileAddOperations();
                
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
                errorMetadata.Add("ErrorMessage", "Not all file writes were completed");
                if (this.diff.FileAddOperations.Count < 10)
                {
                    errorMetadata.Add("RemainingShas", string.Join(",", this.diff.FileAddOperations.Keys));
                }
                else
                {
                    errorMetadata.Add("RemainingShaCount", this.diff.FileAddOperations.Count);
                }

                this.tracer.RelatedError(errorMetadata);
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
                            Directory.CreateDirectory(treeOp.TargetFilename);
                        }
                        catch (Exception ex)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Operation", "CreateDirectory");
                            metadata.Add("Path", treeOp.TargetFilename);
                            metadata.Add("ErrorMessage", ex.Message);
                            this.tracer.RelatedError(metadata);
                            this.HasFailures = true;
                        }

                        break;
                    case DiffTreeResult.Operations.Delete:
                        try
                        {
                            if (Directory.Exists(treeOp.TargetFilename))
                            {
                                PhysicalFileSystem.RecursiveDelete(treeOp.TargetFilename);
                            }
                        }
                        catch (Exception ex)
                        {
                            // We are deleting directories and subdirectories in parallel
                            if (Directory.Exists(treeOp.TargetFilename))
                            {
                                EventMetadata metadata = new EventMetadata();
                                metadata.Add("Operation", "DeleteDirectory");
                                metadata.Add("Path", treeOp.TargetFilename);
                                metadata.Add("ErrorMessage", ex.Message);
                                this.tracer.RelatedError(metadata);
                                this.HasFailures = true;
                            }
                        }

                        break;
                    case DiffTreeResult.Operations.RenameEdit:
                        try
                        {
                            // If target is file, just delete the source.
                            if (!treeOp.TargetIsDirectory)
                            {
                                if (Directory.Exists(treeOp.SourceFilename))
                                {
                                    PhysicalFileSystem.RecursiveDelete(treeOp.SourceFilename);
                                }
                            }
                            else
                            {
                                // If target is directory, delete any source file and add
                                if (!treeOp.SourceIsDirectory)
                                {
                                    if (File.Exists(treeOp.SourceFilename))
                                    {
                                        File.Delete(treeOp.SourceFilename);
                                    }

                                    goto case DiffTreeResult.Operations.Add;
                                }
                                else
                                {
                                    // Source and target are directory, do a move and let later steps handle any sub-edits.
                                    Directory.Move(treeOp.SourceFilename, treeOp.TargetFilename);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Operation", "RenameDirectory");
                            metadata.Add("Path", treeOp.TargetFilename);
                            metadata.Add("ErrorMessage", ex.Message);
                            this.tracer.RelatedError(metadata);
                            this.HasFailures = true;
                        }

                        break;
                    default:
                        this.tracer.RelatedError("Ignoring unexpected Tree Operation {0}: {1}", treeOp.TargetFilename, treeOp.Operation);
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
                    metadata.Add("ErrorMessage", ex.Message);
                    this.tracer.RelatedError(metadata);
                    this.HasFailures = true;
                }
            }
        }

        private void HandleAllFileAddOperations()
        {
            using (LibGit2Repo repo = new LibGit2Repo(this.tracer, this.enlistment.EnlistmentRoot))
            {
                string availableBlob;
                while (this.AvailableBlobShas.TryTake(out availableBlob, millisecondsTimeout: -1))
                {
                    if (this.HasFailures)
                    {
                        return;
                    }
                    
                    Interlocked.Increment(ref this.shasReceived);

                    HashSet<string> paths;
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

                            foreach (string path in paths)
                            {
                                this.AddedOrEditedLocalFiles.Add(path);

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
                            errorData.Add("ErrorMessage", ex.ToString());
                            this.tracer.RelatedError(errorData);
                            this.HasFailures = true;
                        }
                    }
                }
            }
        }
    }
}