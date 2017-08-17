using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Git
{
    public class GVFSGitObjects : GitObjects
    {
        private static readonly TimeSpan NegativeCacheTTL = TimeSpan.FromSeconds(30);

        private ConcurrentDictionary<string, DateTime> objectNegativeCache;

        public GVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor objectRequestor)
            : base(context.Tracer, context.Enlistment, objectRequestor)
        {
            this.Context = context;
            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        protected GVFSContext Context { get; private set; }

        public bool TryCopyBlobContentStream(string sha, Action<Stream, long> writeAction)
        {
            RetryWrapper<bool> retrier = new RetryWrapper<bool>(this.GitObjectRequestor.RetryConfig.MaxAttempts);
            retrier.OnFailure += 
                errorArgs =>
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("sha", sha);
                    metadata.Add("AttemptNumber", errorArgs.TryCount);
                    metadata.Add("WillRetry", errorArgs.WillRetry);
                    metadata.Add("ErrorMessage", "TryCopyBlobContentStream: Failed to provide blob contents");
                    this.Tracer.RelatedError(metadata);
                };

            string firstTwoShaDigits = sha.Substring(0, 2);
            string remainingShaDigits = sha.Substring(2);

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
                        // Pass in 1 for maxAttempts because the retrier in this method manages multiple attempts
                        if (this.TryDownloadAndSaveObject(firstTwoShaDigits, remainingShaDigits, maxAttempts: 1))
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

        public bool TryDownloadAndSaveObject(string firstTwoShaDigits, string remainingShaDigits)
        {
            return this.TryDownloadAndSaveObject(firstTwoShaDigits, remainingShaDigits, GitObjectsHttpRequestor.UseConfiguredMaxAttempts);
        }

        public bool TryDownloadAndSaveObject(string firstTwoShaDigits, string remainingShaDigits, int maxAttempts)
        {
            DateTime negativeCacheRequestTime;
            string objectId = firstTwoShaDigits + remainingShaDigits;

            if (this.objectNegativeCache.TryGetValue(objectId, out negativeCacheRequestTime))
            {
                if (negativeCacheRequestTime > DateTime.Now.Subtract(NegativeCacheTTL))
                {
                    return false;
                }

                this.objectNegativeCache.TryRemove(objectId, out negativeCacheRequestTime);
            }

            DownloadAndSaveObjectResult result = this.TryDownloadAndSaveObject(objectId, maxAttempts);

            switch (result)
            {
                case DownloadAndSaveObjectResult.Success:
                    return true;
                case DownloadAndSaveObjectResult.ObjectNotOnServer:
                    this.objectNegativeCache.AddOrUpdate(objectId, DateTime.Now, (unused1, unused2) => DateTime.Now);
                    return false;
                case DownloadAndSaveObjectResult.Error:
                    return false;
                default:
                    throw new InvalidOperationException("Unknown DownloadAndSaveObjectResult value");
            }
        }

        public bool TryGetBlobSizeLocally(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength(sha, out length);
        }

        public List<GitObjectsHttpRequestor.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds);
        }
    }
}