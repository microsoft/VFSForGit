using GVFS.Common.Prefetch.Git;
using GVFS.Common.Tracing;
using System.Collections.Concurrent;
using System.Threading;

namespace GVFS.Common.Prefetch.Jobs
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

        private BlockingCollection<string> requiredBlobs;

        private ConcurrentHashSet<string> alreadyFoundBlobIds;

        public FindMissingBlobsJob(
            int maxParallel,
            BlockingCollection<string> requiredBlobs,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            Enlistment enlistment)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);
            this.requiredBlobs = requiredBlobs;
            this.enlistment = enlistment;
            this.alreadyFoundBlobIds = new ConcurrentHashSet<string>();

            this.MissingBlobs = new BlockingCollection<string>();
            this.AvailableBlobs = availableBlobs;
        }

        public BlockingCollection<string> MissingBlobs { get; }
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
            using (PrefetchLibGit2Repo repo = new PrefetchLibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryRoot))
            {
                while (this.requiredBlobs.TryTake(out blobId, Timeout.Infinite))
                {
                    if (this.alreadyFoundBlobIds.Add(blobId))
                    {
                        if (!repo.ObjectExists(blobId))
                        {
                            Interlocked.Increment(ref this.missingBlobCount);
                            this.MissingBlobs.Add(blobId);
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
            this.MissingBlobs.CompleteAdding();

            EventMetadata metadata = new EventMetadata();
            metadata.Add("TotalMissingObjects", this.missingBlobCount);
            metadata.Add("AvailableObjects", this.availableBlobCount);
            this.tracer.Stop(metadata);
        }
    }
}
