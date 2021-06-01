using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace GVFS.Common.Git
{
    public class GVFSGitObjects : GitObjects
    {
        private static readonly TimeSpan NegativeCacheTTL = TimeSpan.FromSeconds(30);

        private ConcurrentDictionary<string, DateTime> objectNegativeCache;

        public GVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor objectRequestor)
            : base(context.Tracer, context.Enlistment, objectRequestor, context.FileSystem)
        {
            this.Context = context;
            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        public enum RequestSource
        {
            Invalid = 0,
            FileStreamCallback,
            GVFSVerb,
            NamedPipeMessage,
            SymLinkCreation,
        }

        protected GVFSContext Context { get; private set; }

        public virtual bool TryCopyBlobContentStream(
            string sha,
            CancellationToken cancellationToken,
            RequestSource requestSource,
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

                    if (errorArgs.Error != null)
                    {
                        metadata.Add("Exception", errorArgs.Error.ToString());
                    }

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
                        if (this.TryDownloadAndSaveObject(sha, cancellationToken, requestSource, retryOnFailure: false) == DownloadAndSaveObjectResult.Success)
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

        public DownloadAndSaveObjectResult TryDownloadAndSaveObject(string objectId, RequestSource requestSource)
        {
            return this.TryDownloadAndSaveObject(objectId, CancellationToken.None, requestSource, retryOnFailure: true);
        }

        public bool TryGetBlobSizeLocally(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength(sha, out length);
        }

        public List<GitObjectsHttpRequestor.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds, cancellationToken);
        }

        private DownloadAndSaveObjectResult TryDownloadAndSaveObject(
            string objectId,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            bool retryOnFailure)
        {
            if (objectId == GVFSConstants.AllZeroSha)
            {
                return DownloadAndSaveObjectResult.Error;
            }

            DateTime negativeCacheRequestTime;
            if (this.objectNegativeCache.TryGetValue(objectId, out negativeCacheRequestTime))
            {
                if (negativeCacheRequestTime > DateTime.Now.Subtract(NegativeCacheTTL))
                {
                    return DownloadAndSaveObjectResult.ObjectNotOnServer;
                }

                this.objectNegativeCache.TryRemove(objectId, out negativeCacheRequestTime);
            }

            // To reduce allocations, reuse the same buffer when writing objects in this batch
            byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadLooseObject(
                objectId,
                retryOnFailure,
                cancellationToken,
                requestSource.ToString(),
                onSuccess: (tryCount, response) =>
                {
                    // If the request is from git.exe (i.e. NamedPipeMessage) then we should assume that if there is an
                    // object on disk it's corrupt somehow (which is why git is asking for it)
                    this.WriteLooseObject(
                        response.Stream,
                        objectId,
                        overwriteExistingObject: requestSource == RequestSource.NamedPipeMessage,
                        bufToCopyWith: bufToCopyWith);

                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                });

            if (output.Result != null)
            {
                if (output.Succeeded && output.Result.Success)
                {
                    return DownloadAndSaveObjectResult.Success;
                }

                if (output.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
                {
                    this.objectNegativeCache.AddOrUpdate(objectId, DateTime.Now, (unused1, unused2) => DateTime.Now);
                    return DownloadAndSaveObjectResult.ObjectNotOnServer;
                }
            }

            return DownloadAndSaveObjectResult.Error;
        }
    }
}