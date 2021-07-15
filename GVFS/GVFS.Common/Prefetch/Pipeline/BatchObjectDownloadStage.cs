using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NetworkStreams;
using GVFS.Common.Prefetch.Pipeline.Data;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Prefetch.Pipeline
{
    /// <summary>
    /// Takes in blocks of object shas, downloads object shas as a pack or loose object, outputs pack locations (if applicable).
    /// </summary>
    public class BatchObjectDownloadStage : PrefetchPipelineStage
    {
        private const string AreaPath = nameof(BatchObjectDownloadStage);
        private const string DownloadAreaPath = "Download";

        private static readonly TimeSpan HeartBeatPeriod = TimeSpan.FromSeconds(20);

        private readonly DownloadRequestAggregator downloadRequests;

        private int activeDownloadCount;

        private ITracer tracer;
        private Enlistment enlistment;
        private GitObjectsHttpRequestor objectRequestor;
        private GitObjects gitObjects;
        private Timer heartbeat;

        private long bytesDownloaded = 0;

        public BatchObjectDownloadStage(
            int maxParallel,
            int chunkSize,
            BlockingCollection<string> missingBlobs,
            BlockingCollection<string> availableBlobs,
            ITracer tracer,
            Enlistment enlistment,
            GitObjectsHttpRequestor objectRequestor,
            GitObjects gitObjects)
            : base(maxParallel)
        {
            this.tracer = tracer.StartActivity(AreaPath, EventLevel.Informational, Keywords.Telemetry, metadata: null);

            this.downloadRequests = new DownloadRequestAggregator(missingBlobs, chunkSize);

            this.enlistment = enlistment;
            this.objectRequestor = objectRequestor;

            this.gitObjects = gitObjects;

            this.AvailablePacks = new BlockingCollection<IndexPackRequest>();
            this.AvailableObjects = availableBlobs;
        }

        public BlockingCollection<IndexPackRequest> AvailablePacks { get; }

        public BlockingCollection<string> AvailableObjects { get; }

        protected override void DoBeforeWork()
        {
            this.heartbeat = new Timer(this.EmitHeartbeat, null, TimeSpan.Zero, HeartBeatPeriod);
            base.DoBeforeWork();
        }

        protected override void DoWork()
        {
            BlobDownloadRequest request;
            while (this.downloadRequests.TryTake(out request))
            {
                Interlocked.Increment(ref this.activeDownloadCount);

                EventMetadata metadata = new EventMetadata();
                metadata.Add("RequestId", request.RequestId);
                metadata.Add("ActiveDownloads", this.activeDownloadCount);
                metadata.Add("NumberOfObjects", request.ObjectIds.Count);

                using (ITracer activity = this.tracer.StartActivity(DownloadAreaPath, EventLevel.Informational, Keywords.Telemetry, metadata))
                {
                    try
                    {
                        HashSet<string> successfulDownloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.objectRequestor.TryDownloadObjects(
                                () => request.ObjectIds.Except(successfulDownloads),
                                onSuccess: (tryCount, response) => this.WriteObjectOrPack(request, tryCount, response, successfulDownloads),
                                onFailure: RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.StandardErrorHandler(activity, request.RequestId, DownloadAreaPath),
                                preferBatchedLooseObjects: true);

                        if (!result.Succeeded)
                        {
                            this.HasFailures = true;
                        }

                        metadata.Add("Success", result.Succeeded);
                        metadata.Add("AttemptNumber", result.Attempts);
                        metadata["ActiveDownloads"] = this.activeDownloadCount - 1;
                        activity.Stop(metadata);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref this.activeDownloadCount);
                    }
                }
            }
        }

        protected override void DoAfterWork()
        {
            this.heartbeat.Dispose();
            this.heartbeat = null;

            this.AvailablePacks.CompleteAdding();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RequestCount", BlobDownloadRequest.TotalRequests);
            metadata.Add("BytesDownloaded", this.bytesDownloaded);
            this.tracer.Stop(metadata);
        }

        private RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult WriteObjectOrPack(
            BlobDownloadRequest request,
            int tryCount,
            GitEndPointResponseData response,
            HashSet<string> successfulDownloads = null)
        {
            // To reduce allocations, reuse the same buffer when writing objects in this batch
            byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

            string fileName = null;
            switch (response.ContentType)
            {
                case GitObjectContentType.LooseObject:
                    string sha = request.ObjectIds.First();
                    fileName = this.gitObjects.WriteLooseObject(
                        response.Stream,
                        sha,
                        overwriteExistingObject: false,
                        bufToCopyWith: bufToCopyWith);
                    this.AvailableObjects.Add(sha);
                    break;
                case GitObjectContentType.PackFile:
                    fileName = this.gitObjects.WriteTempPackFile(response.Stream);
                    this.AvailablePacks.Add(new IndexPackRequest(fileName, request));
                    break;
                case GitObjectContentType.BatchedLooseObjects:
                    BatchedLooseObjectDeserializer.OnLooseObject onLooseObject = (objectStream, sha1) =>
                    {
                        this.gitObjects.WriteLooseObject(
                            objectStream,
                            sha1,
                            overwriteExistingObject: false,
                            bufToCopyWith: bufToCopyWith);
                        this.AvailableObjects.Add(sha1);

                        if (successfulDownloads != null)
                        {
                            successfulDownloads.Add(sha1);
                        }

                        // This isn't strictly correct because we don't add object header bytes,
                        // just the actual compressed content length, but we expect the amount of
                        // header data to be negligible compared to the objects themselves.
                        Interlocked.Add(ref this.bytesDownloaded, objectStream.Length);
                    };

                    new BatchedLooseObjectDeserializer(response.Stream, onLooseObject).ProcessObjects();
                    break;
            }

            if (fileName != null)
            {
                // NOTE: If we are writing a file as part of this method, the only case
                // where it's not expected to exist is when running unit tests
                FileInfo info = new FileInfo(fileName);
                if (info.Exists)
                {
                    Interlocked.Add(ref this.bytesDownloaded, info.Length);
                }
                else
                {
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                        new GitObjectsHttpRequestor.GitObjectTaskResult(false));
                }
            }

            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                new GitObjectsHttpRequestor.GitObjectTaskResult(true));
        }

        private void EmitHeartbeat(object state)
        {
            EventMetadata metadata = new EventMetadata();
            metadata["ActiveDownloads"] = this.activeDownloadCount;
            this.tracer.RelatedEvent(EventLevel.Verbose, "DownloadHeartbeat", metadata);
        }

        private class DownloadRequestAggregator
        {
            private BlockingCollection<string> missingBlobs;
            private int chunkSize;

            public DownloadRequestAggregator(BlockingCollection<string> missingBlobs, int chunkSize)
            {
                this.missingBlobs = missingBlobs;
                this.chunkSize = chunkSize;
            }

            public bool TryTake(out BlobDownloadRequest request)
            {
                List<string> blobsInChunk = new List<string>();

                for (int i = 0; i < this.chunkSize;)
                {
                    // Only wait a short while for new work to show up, otherwise go ahead and download what we have accumulated so far
                    const int TimeoutMs = 100;

                    string blobId;
                    if (this.missingBlobs.TryTake(out blobId, TimeoutMs))
                    {
                        blobsInChunk.Add(blobId);

                        // Only increment if a blob was added. Otherwise, if no blobs are added during TimeoutMs * chunkSize,
                        // this will exit early and blobs added later will not be downloaded.
                        ++i;
                    }
                    else if (blobsInChunk.Count > 0 ||
                        this.missingBlobs.IsAddingCompleted)
                    {
                        break;
                    }
                }

                if (blobsInChunk.Count > 0)
                {
                    request = new BlobDownloadRequest(blobsInChunk);
                    return true;
                }

                request = null;
                return false;
            }
        }
    }
}