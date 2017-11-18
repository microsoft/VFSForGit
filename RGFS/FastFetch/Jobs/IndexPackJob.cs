using FastFetch.Jobs.Data;
using RGFS.Common.Git;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System.Collections.Concurrent;
using System.Threading;

namespace FastFetch.Jobs
{
    public class IndexPackJob : Job
    {
        private const string AreaPath = "IndexPackJob";
        private const string IndexPackAreaPath = "IndexPack";

        private readonly BlockingCollection<IndexPackRequest> availablePacks;

        private ITracer tracer;
        private GitObjects gitObjects;

        private long shasIndexed = 0;

        public IndexPackJob(
            int maxParallel,
            BlockingCollection<IndexPackRequest> availablePacks,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            GitObjects gitObjects)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);
            this.availablePacks = availablePacks;
            this.gitObjects = gitObjects;
            this.AvailableBlobs = availableBlobs;
        }

        public BlockingCollection<string> AvailableBlobs { get; }

        protected override void DoWork()
        {
            IndexPackRequest request;
            while (this.availablePacks.TryTake(out request, Timeout.Infinite))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("PackId", request.DownloadRequest.PackId);
                using (ITracer activity = this.tracer.StartActivity(IndexPackAreaPath, EventLevel.Informational, Keywords.Telemetry, metadata))
                {
                    GitProcess.Result result = this.gitObjects.IndexTempPackFile(request.TempPackFile);
                    if (result.HasErrors)
                    {
                        EventMetadata errorMetadata = new EventMetadata();
                        errorMetadata.Add("PackId", request.DownloadRequest.PackId);
                        activity.RelatedError(errorMetadata, result.Errors);
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
