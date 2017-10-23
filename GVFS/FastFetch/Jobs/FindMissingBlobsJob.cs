using FastFetch.Git;
using GVFS.Common;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System.Collections.Concurrent;
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

        public FindMissingBlobsJob(
            int maxParallel,
            BlockingCollection<string> inputQueue,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            Enlistment enlistment)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);
            this.inputQueue = inputQueue;
            this.enlistment = enlistment;
            this.alreadyFoundBlobIds = new ConcurrentHashSet<string>();

            this.DownloadQueue = new BlockingCollection<string>();
            this.AvailableBlobs = availableBlobs;
        }

        public BlockingCollection<string> DownloadQueue { get; }
        public BlockingCollection<string> AvailableBlobs { get; }

        public int MissingBlobCount
        {
            get { return this.missingBlobCount; }
        }

        public int AvailableBlobCount
        {
            get { return this.availableBlobCount; }
        }

        protected override void DoWork()
        {
            string blobId;
            using (FastFetchLibGit2Repo repo = new FastFetchLibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryRoot))
            {
                while (this.inputQueue.TryTake(out blobId, Timeout.Infinite))
                {
                    if (this.alreadyFoundBlobIds.Add(blobId))
                    {
                        if (!repo.ObjectExists(blobId))
                        {
                            Interlocked.Increment(ref this.missingBlobCount);
                            this.DownloadQueue.Add(blobId);
                        }
                        else
                        {
                            Interlocked.Increment(ref this.availableBlobCount);
                            this.AvailableBlobs.Add(blobId);
                        }
                    }
                }
            }
        }

        protected override void DoAfterWork()
        {
            this.DownloadQueue.CompleteAdding();

            EventMetadata metadata = new EventMetadata();
            metadata.Add("TotalMissingObjects", this.missingBlobCount);
            metadata.Add("AvailableObjects", this.availableBlobCount);
            this.tracer.Stop(metadata);
        }
    }
}
