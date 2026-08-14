using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;

namespace GVFS.Common.Git
{
    public class GVFSGitObjects : GitObjects
    {
        private static readonly TimeSpan NegativeCacheTTL = TimeSpan.FromSeconds(30);

        private ConcurrentDictionary<string, DateTime> objectNegativeCache;
        internal ConcurrentDictionary<string, Lazy<DownloadAttemptResult>> inflightDownloads;

        public GVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor objectRequestor)
            : base(context.Tracer, context.Enlistment, objectRequestor, context.FileSystem)
        {
            this.Context = context;
            this.objectNegativeCache = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            this.inflightDownloads = new ConcurrentDictionary<string, Lazy<DownloadAttemptResult>>(StringComparer.OrdinalIgnoreCase);
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

        /// <summary>
        /// Carries the outcome of an object download together with the HTTP status of the last
        /// download attempt. The public <see cref="DownloadAndSaveObjectResult"/> enum only records
        /// success/not-found/error, which collapses genuine auth failures (401/400/302) and
        /// transient failures (408/5xx/pool-exhaustion 503) into a single "error" outcome. The
        /// status is retained here so the terminal blob-hydration telemetry can tell them apart.
        /// </summary>
        internal class DownloadAttemptResult
        {
            public DownloadAttemptResult(DownloadAndSaveObjectResult result, HttpStatusCode? httpStatusCode)
            {
                this.Result = result;
                this.HttpStatusCode = httpStatusCode;
            }

            public DownloadAndSaveObjectResult Result { get; }

            // The HTTP status of the last download attempt, or null when no HTTP response was
            // received (for example an exhausted retry that ended in an exception).
            public HttpStatusCode? HttpStatusCode { get; }
        }

        protected GVFSContext Context { get; private set; }

        public virtual bool TryCopyBlobContentStream(
            string sha,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            Action<Stream, long> writeAction,
            out BlobHydrationFailureCategory failureCategory)
        {
            // Short-circuit a malformed SHA (for example a corrupt placeholder's all-NUL
            // content-id) before the retry loop. GitRepo already rejects it as a clean miss,
            // but a bogus SHA can never be downloaded either (the server returns 404), so
            // attempting it would only produce doomed download retries. Because the read is
            // never satisfied, the caller re-requests it endlessly, which turns one corrupt
            // placeholder into an unbounded error/retry storm. Fail fast and cheap instead.
            // The cause stays categorized as Unexpected (no dedicated category); the caller
            // tags its terminal telemetry from failureCategory below.
            if (!SHA1Util.IsValidShaFormat(sha))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("sha", SHA1Util.ToLoggableShaString(sha));
                metadata.Add("RequestSource", requestSource.ToString());
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "TryCopyBlobContentStream: Refusing to hydrate blob with malformed SHA");
                this.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.TryCopyBlobContentStream) + "_MalformedBlobSha", metadata, Keywords.Telemetry);

                failureCategory = BlobHydrationFailureCategory.Unexpected;
                return false;
            }

            // Track the outcome of the most recent attempt so that the terminal failure
            // telemetry can attribute the failure to a cause (network vs. object-missing vs.
            // local copy) that is otherwise collapsed into the bool return value below. The
            // final category is also surfaced via the out parameter so the caller can tag its
            // own terminal telemetry with the same cause.
            DownloadAttemptResult lastDownloadResult = null;
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

                        // A RetryableException wraps its real cause in InnerException, so inspect the
                        // inner exception rather than the RetryableException type. On this branch the
                        // exception arrives from Context.Repository.TryCopyBlobContentStream - typically
                        // StreamUtil wrapping an IOException while reading a corrupt/truncated local
                        // loose object (UnauthorizedAccessException/Win32Exception are treated the same
                        // as they belong to the local disk/IO family). Without this unwrap every
                        // RetryableException - the single largest hydration-failure bucket in the field -
                        // is misattributed to NetworkUnavailable even when the cause is local disk/IO. A
                        // stream-read IOException can still originate in the download layer, but we cannot
                        // tell where it came from, so it is bucketed as local IO.
                        Exception rootError = (errorArgs.Error as RetryableException)?.InnerException ?? errorArgs.Error;
                        category = rootError is IOException || rootError is UnauthorizedAccessException || rootError is Win32Exception
                            ? BlobHydrationFailureCategory.LocalIO
                            : BlobHydrationFailureCategory.NetworkUnavailable;
                    }
                    else if (downloadSucceededButCopyFailed)
                    {
                        category = BlobHydrationFailureCategory.LocalCopyFailed;
                    }
                    else if (lastDownloadResult?.Result == DownloadAndSaveObjectResult.ObjectNotOnServer)
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

                    // Surface the HTTP status of the last download attempt so telemetry can tell a
                    // genuine auth failure (401/400/302) apart from a transient one (408/5xx/503),
                    // both of which otherwise land in the DownloadFailed bucket. Only attach it when
                    // the failure is attributable to the download itself (DownloadFailed or
                    // ObjectNotOnServer). On the exception (LocalIO/NetworkUnavailable) and
                    // LocalCopyFailed paths lastDownloadResult can hold a status captured on an
                    // earlier attempt, so the status would be stale and misattribute the failure.
                    bool statusIsAttributable =
                        category == BlobHydrationFailureCategory.DownloadFailed ||
                        category == BlobHydrationFailureCategory.ObjectNotOnServer;
                    if (statusIsAttributable && lastDownloadResult?.HttpStatusCode != null)
                    {
                        metadata.Add("HttpStatusCode", (int)lastDownloadResult.HttpStatusCode.Value);
                        metadata.Add("HttpStatusName", lastDownloadResult.HttpStatusCode.Value.ToString());
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
                        downloadSucceededButCopyFailed = false;

                        // Pass in false for retryOnFailure because the retrier in this method manages multiple attempts
                        lastDownloadResult = this.TryDownloadAndSaveObject(sha, cancellationToken, requestSource, retryOnFailure: false);
                        if (lastDownloadResult.Result == DownloadAndSaveObjectResult.Success)
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
            return this.TryDownloadAndSaveObject(objectId, CancellationToken.None, requestSource, retryOnFailure: true).Result;
        }

        public bool TryGetBlobSizeLocally(string sha, out long length)
        {
            return this.Context.Repository.TryGetBlobLength(sha, out length);
        }

        public List<GitObjectsHttpRequestor.GitObjectSize> GetFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            return this.GitObjectRequestor.QueryForFileSizes(objectIds, cancellationToken);
        }

        private DownloadAttemptResult TryDownloadAndSaveObject(
            string objectId,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            bool retryOnFailure)
        {
            // Defense in depth for a malformed object id (for example a corrupt placeholder's
            // all-NUL content-id). On .NET Framework Path.Combine threw ArgumentException on
            // such a value; on modern .NET it does not, so a malformed SHA silently misses the
            // local object store and would otherwise be sent to the cache server, which rejects
            // the URL with HTTP 400 - and GVFS then erases a valid credential (HttpRequestor
            // treats 400 as an auth failure), producing a credential-prompt storm. Callers other
            // than blob hydration reach this method WITHOUT going through the
            // TryCopyBlobContentStream guard - the git.exe read-object hook (NamedPipeMessage,
            // via InProcessMount) and the gitattributes GVFSVerb - so reject a malformed SHA here
            // for every caller before any request is built.
            if (!SHA1Util.IsValidShaFormat(objectId))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("sha", SHA1Util.ToLoggableShaString(objectId));
                metadata.Add("RequestSource", requestSource.ToString());
                metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.TryDownloadAndSaveObject) + ": Refusing to download object with malformed SHA");
                this.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.TryDownloadAndSaveObject) + "_MalformedBlobSha", metadata, Keywords.Telemetry);

                return DownloadAndSaveObjectResult.Error;
            }

            if (objectId == GVFSConstants.AllZeroSha)
            {
                return new DownloadAttemptResult(DownloadAndSaveObjectResult.Error, httpStatusCode: null);
            }

            DateTime negativeCacheRequestTime;
            if (this.objectNegativeCache.TryGetValue(objectId, out negativeCacheRequestTime))
            {
                if (negativeCacheRequestTime > DateTime.Now.Subtract(NegativeCacheTTL))
                {
                    return new DownloadAttemptResult(DownloadAndSaveObjectResult.ObjectNotOnServer, httpStatusCode: null);
                }

                this.objectNegativeCache.TryRemove(objectId, out negativeCacheRequestTime);
            }

            // Coalesce concurrent requests for the same objectId so that only one HTTP
            // download runs per SHA at a time. All concurrent callers share the result.
            // Note: the first caller's cancellationToken and retryOnFailure settings are
            // captured by the Lazy factory. Subsequent coalesced callers inherit those
            // settings. In practice this is fine because the primary concurrent path
            // (NamedPipeMessage from git.exe) always uses CancellationToken.None.
            Lazy<DownloadAttemptResult> newLazy = new Lazy<DownloadAttemptResult>(
                () => this.DoDownloadAndSaveObject(objectId, cancellationToken, requestSource, retryOnFailure));
            Lazy<DownloadAttemptResult> lazy = this.inflightDownloads.GetOrAdd(objectId, newLazy);

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
        private bool TryRemoveInflightDownload(string objectId, Lazy<DownloadAttemptResult> lazy)
        {
            return ((ICollection<KeyValuePair<string, Lazy<DownloadAttemptResult>>>)this.inflightDownloads)
                .Remove(new KeyValuePair<string, Lazy<DownloadAttemptResult>>(objectId, lazy));
        }

        private DownloadAttemptResult DoDownloadAndSaveObject(
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

            // Capture the HTTP status of the last download attempt when a response was received.
            // On failure the requestor propagates the real status (e.g. 401/404/503); on an
            // exhausted retry that ended in an exception output.Result is null and no status is
            // known. A default (zero) status means the result carried no HTTP response, so it is
            // treated as "no status".
            HttpStatusCode? httpStatusCode = null;
            if (output.Result != null && output.Result.HttpStatusCodeResult != 0)
            {
                httpStatusCode = output.Result.HttpStatusCodeResult;
            }

            if (output.Result != null)
            {
                if (output.Succeeded && output.Result.Success)
                {
                    return new DownloadAttemptResult(DownloadAndSaveObjectResult.Success, httpStatusCode);
                }

                if (output.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
                {
                    this.objectNegativeCache.AddOrUpdate(objectId, DateTime.Now, (unused1, unused2) => DateTime.Now);
                    return new DownloadAttemptResult(DownloadAndSaveObjectResult.ObjectNotOnServer, httpStatusCode);
                }
            }

            return new DownloadAttemptResult(DownloadAndSaveObjectResult.Error, httpStatusCode);
        }
    }
}