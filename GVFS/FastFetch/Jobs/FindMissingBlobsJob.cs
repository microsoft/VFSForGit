using FastFetch.Jobs.Data;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FastFetch.Jobs
{
    /// <summary>
    /// Takes in search requests, searches each tree as requested, outputs blocks of missing blob shas.
    /// </summary>
    public class FindMissingBlobsJob : Job
    {
        private const string AreaPath = nameof(FindMissingBlobsJob);
        private const string TreeSearchAreaPath = "TreeSearch";
        
        private ITracer tracer;
        private Enlistment enlistment;
        private int missingBlobCount;
        private int availableBlobCount;
        
        private BlockingCollection<string> inputQueue;

        private ConcurrentHashSet<string> alreadyFoundBlobIds;
        private ProcessPool<GitCatFileBatchCheckProcess> catFilePool;

        public FindMissingBlobsJob(
            int maxParallel,
            BlockingCollection<string> inputQueue,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            Enlistment enlistment)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational);
            this.inputQueue = inputQueue;
            this.enlistment = enlistment;
            this.alreadyFoundBlobIds = new ConcurrentHashSet<string>();
            
            this.DownloadQueue = new BlockingCollection<string>();
            this.AvailableBlobs = availableBlobs;

            this.catFilePool = new ProcessPool<GitCatFileBatchCheckProcess>(
                tracer,
                () => new GitCatFileBatchCheckProcess(this.tracer, this.enlistment),
                maxParallel);
        }

        public BlockingCollection<string> DownloadQueue { get; }
        public BlockingCollection<string> AvailableBlobs { get; }

        protected override void DoWork()
        {
            string blobId;
            while (this.inputQueue.TryTake(out blobId, Timeout.Infinite))
            {
                this.catFilePool.Invoke(catFileProcess =>
                {
                    if (!catFileProcess.ObjectExists_CanTimeout(blobId))
                    {
                        Interlocked.Increment(ref this.missingBlobCount);
                        this.DownloadQueue.Add(blobId);
                    }
                    else
                    {
                        Interlocked.Increment(ref this.availableBlobCount);
                        this.AvailableBlobs.Add(blobId);
                    }
                });
            }
        }

        protected override void DoAfterWork()
        {
            this.DownloadQueue.CompleteAdding();
            this.catFilePool.Dispose();

            EventMetadata metadata = new EventMetadata();
            metadata.Add("TotalMissingObjects", this.missingBlobCount);
            metadata.Add("AvailableObjects", this.availableBlobCount);
            this.tracer.Stop(metadata);
        }
    }
}
