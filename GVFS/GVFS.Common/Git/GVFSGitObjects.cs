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
        internal ConcurrentDictionary<string, Lazy<DownloadAndSaveObjectResult>> inflightDownloads;

        public GVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor objectRequestor)
            : base(context.Tracer, context.Enlistment, objectRequestor, context.FileSystem)
        {
            this.Context = context;
            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            this.inflightDownloads = new ConcurrentDictionary<string, Lazy<DownloadAndSaveObjectResult>>(StringComparer.OrdinalIgnoreCase);
        }

        public enum RequestSource
        {
            Invalid = 0,
            FileStreamCallback,
            GVFSVerb,
            NamedPipeMessage,
            SymLinkCreation,
        }

        /// <summary>
        /// Why a blob-hydration request ultimately failed. Recorded on the terminal failure
        /// telemetry so failures outside gvfs.exe's control (network, local disk/IO, ProjFS)
        /// can be told apart from failures that point at an actionable bug or a server/data
        /// problem. Kept in sync with the telemetry bucketing in the devprod.git.telemetry
        /// workbook (gvfs-regression-signatures.kql).
        /// </summary>
        public enum BlobHydrationFailureCategory
        {
            None = 0,

            // Outside gvfs.exe's control:
            NetworkUnavailable,   // A network/HTTP-layer exception while fetching the blob.
            DownloadFailed,       // The blob download reported failure (transient/unclassified).
            LocalIO,              // IOException reading the local object or streaming to the ProjFS buffer.
            ProjFSWriteFailed,    // ProjFS WriteFileData returned a non-recoverable error.

            // Actionable (bug, corruption, or server/data problem):
            ObjectNotOnServer,    // The cache server returned 404 for the blob.
            LocalCopyFailed,      // Blob downloaded, but the subsequent local copy still failed.
            SizeMismatch,         // Blob length did not match the length ProjFS requested.
            Unexpected,           // Unclassified exception.
        }

        protected GVFSContext Context { get; private set; }

        public virtual bool TryCopyBlobContentStream(
            string sha,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            Action<Stream, long> writeAction,
            out BlobHydrationFailureCategory failureCategory)
        {
            // Track the outcome of the most recent attempt so that the terminal failure
            // telemetry can attribute the failure to a cause (network vs. object-missing vs.
            // local copy) that is otherwise collapsed into the bool return value below. The
            // final category is also surfaced via the out parameter so the caller can tag its
            // own terminal telemetry with the same cause.
            DownloadAndSaveObjectResult lastDownloadResult = DownloadAndSaveObjectResult.Error;
            bool downloadSucceededButCopyFailed = false;
            BlobHydrationFailureCategory capturedCategory = BlobHydrationFailureCategory.None;

            RetryWrapper<bool> retrier = new RetryWrapper<bool>(this.GitObjectRequestor.RetryConfig.MaxAttempts, cancellationToken);
            retrier.OnFailure +=
                errorArgs =>
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("sha", sha);
                    metadata.Add("AttemptNumber", errorArgs.TryCount);
                    metadata.Add("WillRetry", errorArgs.WillRetry);

                    BlobHydrationFailureCategory category;
                    if (errorArgs.Error != null)
                    {
                        metadata.Add("Exception", errorArgs.Error.ToString());

                        // An IOException here can also originate in the download/network layer, but
                        // we cannot tell where it came from, so it is bucketed as local IO.
                        category = errorArgs.Error is IOException
                            ? BlobHydrationFailureCategory.LocalIO
                            : BlobHydrationFailureCategory.NetworkUnavailable;
                    }
                    else if (downloadSucceededButCopyFailed)
                    {
                        category = BlobHydrationFailureCategory.LocalCopyFailed;
                    }
                    else if (lastDownloadResult == DownloadAndSaveObjectResult.ObjectNotOnServer)
                    {
                        category = BlobHydrationFailureCategory.ObjectNotOnServer;
                    }
                    else
                    {
                        // The download reported failure without an exception; the cause (network,
                        // disk-save, etc.) is unclassified, so use the neutral DownloadFailed bucket
                        // rather than over-asserting NetworkUnavailable.
                        category = BlobHydrationFailureCategory.DownloadFailed;
                    }

                    capturedCategory = category;
                    metadata.Add(nameof(BlobHydrationFailureCategory), category.ToString());

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
                        downloadSucceededButCopyFailed = false;

                        // Pass in false for retryOnFailure because the retrier in this method manages multiple attempts
                        lastDownloadResult = this.TryDownloadAndSaveObject(sha, cancellationToken, requestSource, retryOnFailure: false);
                        if (lastDownloadResult == DownloadAndSaveObjectResult.Success)
                        {
                            if (this.Context.Repository.TryCopyBlobContentStream(sha, writeAction))
                            {
                                return new RetryWrapper<bool>.CallbackResult(true);
                            }

                            downloadSucceededButCopyFailed = true;
                        }

                        return new RetryWrapper<bool>.CallbackResult(error: null, shouldRetry: true);
                    }
                });

            failureCategory = invokeResult.Result ? BlobHydrationFailureCategory.None : capturedCategory;
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

            // Coalesce concurrent requests for the same objectId so that only one HTTP
            // download runs per SHA at a time. All concurrent callers share the result.
            // Note: the first caller's cancellationToken and retryOnFailure settings are
            // captured by the Lazy factory. Subsequent coalesced callers inherit those
            // settings. In practice this is fine because the primary concurrent path
            // (NamedPipeMessage from git.exe) always uses CancellationToken.None.
            Lazy<DownloadAndSaveObjectResult> newLazy = new Lazy<DownloadAndSaveObjectResult>(
                () => this.DoDownloadAndSaveObject(objectId, cancellationToken, requestSource, retryOnFailure));
            Lazy<DownloadAndSaveObjectResult> lazy = this.inflightDownloads.GetOrAdd(objectId, newLazy);

            if (!ReferenceEquals(lazy, newLazy))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("objectId", objectId);
                metadata.Add("requestSource", requestSource.ToString());
                this.Context.Tracer.RelatedEvent(EventLevel.Informational, "TryDownloadAndSaveObject_CoalescedRequest", metadata);
            }

            try
            {
                return lazy.Value;
            }
            finally
            {
                this.TryRemoveInflightDownload(objectId, lazy);
            }
        }

        /// <summary>
        /// Removes the inflight download entry only if the current value matches the
        /// expected Lazy instance. This prevents an ABA race where a straggling thread's
        /// finally block could remove a newer Lazy created by a later wave of requests.
        /// Uses ICollection&lt;KVP&gt;.Remove which is the value-aware atomic removal on
        /// .NET Framework 4.7.1. When we upgrade to .NET 10 (backlog), this can be
        /// replaced with ConcurrentDictionary.TryRemove(KeyValuePair).
        /// </summary>
        private bool TryRemoveInflightDownload(string objectId, Lazy<DownloadAndSaveObjectResult> lazy)
        {
            return ((ICollection<KeyValuePair<string, Lazy<DownloadAndSaveObjectResult>>>)this.inflightDownloads)
                .Remove(new KeyValuePair<string, Lazy<DownloadAndSaveObjectResult>>(objectId, lazy));
        }

        private DownloadAndSaveObjectResult DoDownloadAndSaveObject(
            string objectId,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            bool retryOnFailure)
        {
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