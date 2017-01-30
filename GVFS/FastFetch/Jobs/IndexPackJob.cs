using FastFetch.Jobs.Data;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System.Collections.Concurrent;
using System.Threading;

namespace FastFetch.Jobs
{
    public class IndexPackJob : Job
    {
        private const string AreaPath = "IndexPackJob";
        private const string IndexPackAreaPath = "IndexPack";

        private readonly BlockingCollection<IndexPackRequest> inputQueue;

        private ITracer tracer;
        private GitObjects gitObjects;

        private long shasIndexed = 0;

        public IndexPackJob(
            int maxParallel,
            BlockingCollection<IndexPackRequest> inputQueue,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            GitObjects gitObjects)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational);
            this.inputQueue = inputQueue;
            this.gitObjects = gitObjects;
            this.AvailableBlobs = availableBlobs;
        }

        public BlockingCollection<string> AvailableBlobs { get; }

        protected override void DoWork()
        {
            IndexPackRequest request;
            while (this.inputQueue.TryTake(out request, millisecondsTimeout: -1))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("PackId", request.DownloadRequest.PackId);
                using (ITracer activity = this.tracer.StartActivity(IndexPackAreaPath, EventLevel.Informational, metadata))
                {
                    GitProcess.Result result = this.gitObjects.IndexTempPackFile(request.TempPackFile);
                    if (result.HasErrors)
                    {
                        EventMetadata errorMetadata = new EventMetadata();
                        errorMetadata.Add("PackId", request.DownloadRequest.PackId);
                        errorMetadata.Add("ErrorMessage", result.Errors);
                        activity.RelatedError(errorMetadata);
                        this.HasFailures = true;
                    }

                    if (!this.HasFailures)
                    {
                        foreach (string blobId in request.DownloadRequest.ObjectIds)
                        {
                            this.AvailableBlobs.Add(blobId);
                            Interlocked.Increment(ref this.shasIndexed);
                        }
                    }

                    metadata.Add("Success", !this.HasFailures);
                    activity.Stop(metadata);
                }
            }
        }

        protected override void DoAfterWork()
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ShasIndexed", this.shasIndexed);
            this.tracer.Stop(metadata);
        }
    }
}
