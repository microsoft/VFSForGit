using RGFS.Common.Http;
using RGFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace RGFS.Common.Git
{
    public class RGFSGitObjects : GitObjects
    {
        private static readonly TimeSpan NegativeCacheTTL = TimeSpan.FromSeconds(30);

        private ConcurrentDictionary<string, DateTime> objectNegativeCache;
        
        public RGFSGitObjects(RGFSContext context, GitObjectsHttpRequestor objectRequestor)
            : base(context.Tracer, context.Enlistment, objectRequestor, context.FileSystem)
        {
            this.Context = context;
            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        protected RGFSContext Context { get; private set; }

        public virtual bool TryCopyBlobContentStream(
            string sha, 
            CancellationToken cancellationToken, 
            Action<Stream, long> writeAction)
        {
            RetryWrapper<bool> retrier = new RetryWrapper<bool>(this.GitObjectRequestor.RetryConfig.MaxAttempts, cancellationToken);
            retrier.OnFailure += 
                errorArgs =>
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("sha", sha);
                    metadata.Add("AttemptNumber", errorArgs.TryCount);
                    metadata.Add("WillRetry", errorArgs.WillRetry);

                    string message = "TryCopyBlobContentStream: Failed to provide blob contents";
                    if (errorArgs.WillRetry)
                    {
                        this.Tracer.RelatedWarning(metadata, message, Keywords.Telemetry);
                    }
                    else
                    {
                        this.Tracer.RelatedError(metadata, message);
                    }
                };
            
            RetryWrapper<bool>.InvocationResult invokeResult = retrier.Invoke(
                tryCount =>
                {
                    bool success = this.Context.Repository.TryCopyBlobContentStream(sha, writeAction);
                    if (success)
                    {
                        return new RetryWrapper<bool>.CallbackResult(true);
                    }
                    else
                    {
                        // Pass in false for retryOnFailure because the retrier in this method manages multiple attempts
                        if (this.TryDownloadAndSaveObject(sha, cancellationToken, retryOnFailure: false) == DownloadAndSaveObjectResult.Success)
                        {
                            if (this.Context.Repository.TryCopyBlobContentStream(sha, writeAction))
                            {
                                return new RetryWrapper<bool>.CallbackResult(true);
                            }
                        }

                        return new RetryWrapper<bool>.CallbackResult(error: null, shouldRetry: true);
                    }
                });

            return invokeResult.Result;
        }

        public DownloadAndSaveObjectResult TryDownloadAndSaveObject(string objectId)
        {
            return this.TryDownloadAndSaveObject(objectId, new CancellationToken(canceled: false), retryOnFailure: true);
        }

        public bool TryGetBlobSizeLocally(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength(sha, out length);
        }

        public List<GitObjectsHttpRequestor.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds, cancellationToken);
        }

        protected override DownloadAndSaveObjectResult TryDownloadAndSaveObject(string objectId, CancellationToken cancellationToken, bool retryOnFailure)
        {
            DateTime negativeCacheRequestTime;
            if (this.objectNegativeCache.TryGetValue(objectId, out negativeCacheRequestTime))
            {
                if (negativeCacheRequestTime > DateTime.Now.Subtract(NegativeCacheTTL))
                {
                    return DownloadAndSaveObjectResult.ObjectNotOnServer;
                }

                this.objectNegativeCache.TryRemove(objectId, out negativeCacheRequestTime);
            }

            DownloadAndSaveObjectResult result = base.TryDownloadAndSaveObject(objectId, cancellationToken, retryOnFailure);

            if (result == DownloadAndSaveObjectResult.ObjectNotOnServer)
            {
                this.objectNegativeCache.AddOrUpdate(objectId, DateTime.Now, (unused1, unused2) => DateTime.Now);
            }

            return result;
        }
    }
}