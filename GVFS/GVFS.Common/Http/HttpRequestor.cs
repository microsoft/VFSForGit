using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Http
{
    public abstract class HttpRequestor : IDisposable
    {
        private const int ConnectionPoolWaitTimeoutMs = 30_000;
        private const int ConnectionPoolContentionThresholdMs = 100;

        private static long requestCount = 0;
        private static SemaphoreSlim availableConnections;
        private static int connectionLimitConfigured = 0;
        private static bool releaseConnectionBeforeCredentialReject = GVFSConstants.GitConfig.ReleaseConnectionBeforeCredentialRejectDefault;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private readonly GitAuthentication authentication;

        private HttpClient client;

        static HttpRequestor()
        {
            // HTTP downloads are I/O-bound, not CPU-bound, so we default to
            // 2x ProcessorCount. Can be overridden via gvfs.max-http-connections.
            int connectionLimit = 2 * Environment.ProcessorCount;
            availableConnections = new SemaphoreSlim(connectionLimit);
        }

        protected HttpRequestor(ITracer tracer, RetryConfig retryConfig, Enlistment enlistment)
            : this(tracer, retryConfig, enlistment, handlerOverride: null)
        {
        }

        /// <summary>
        /// Test-only constructor that injects a custom <see cref="HttpMessageHandler"/> so
        /// <see cref="SendRequest"/> can be exercised without real network I/O. Production code
        /// uses the parameterless-handler overload, which builds a configured
        /// <see cref="SocketsHttpHandler"/>.
        /// </summary>
        internal HttpRequestor(ITracer tracer, RetryConfig retryConfig, Enlistment enlistment, HttpMessageHandler handlerOverride)
        {
            this.RetryConfig = retryConfig;

            this.authentication = enlistment.Authentication;

            this.Tracer = tracer;

            // On first instantiation, check git config for a custom connection limit.
            // This runs before any requests are made (during mount initialization).
            if (Interlocked.CompareExchange(ref connectionLimitConfigured, 1, 0) == 0)
            {
                TryApplyConnectionLimitFromConfig(tracer, enlistment);
                TryApplyReleaseConnectionBeforeRejectFromConfig(tracer, enlistment);
            }

            // WARNING: Do NOT set Credentials or ServerCredentials on this handler.
            //
            // Setting Credentials = CredentialCache.DefaultCredentials causes the handler
            // to perform an NTLM/Negotiate challenge-response on every new connection.
            // On SocketsHttpHandler this adds ~400ms per request vs ~14ms without.
            //
            // GVFS cache servers and Azure DevOps accept PAT/OAuth tokens via the
            // "Authorization: Basic <base64>" header that SendRequest already attaches.
            // Transport-level credentials are redundant and purely wasteful.
            HttpMessageHandler handler;
            if (handlerOverride != null)
            {
                handler = handlerOverride;
            }
            else
            {
                SocketsHttpHandler socketsHandler = new SocketsHttpHandler()
                {
                    MaxConnectionsPerServer = Environment.ProcessorCount,
                    PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                };

                this.authentication.ConfigureSocketsHandlerSslIfNeeded(this.Tracer, socketsHandler, enlistment.CreateGitProcess());
                handler = socketsHandler;
            }

            this.client = new HttpClient(handler)
            {
                Timeout = retryConfig.Timeout,
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            this.userAgentHeader = new ProductInfoHeaderValue(ProcessHelper.GetEntryClassName(), ProcessHelper.GetCurrentProcessVersion());
        }

        public RetryConfig RetryConfig { get; }

        protected ITracer Tracer { get; }

        /// <summary>
        /// Number of currently-available connection-pool permits. Test-only observability hook
        /// for asserting that <see cref="SendRequest"/> releases its slot at the right time.
        /// </summary>
        internal static int AvailableConnectionCount => availableConnections.CurrentCount;

        /// <summary>
        /// When true, <see cref="SendRequest"/> releases the connection-pool slot before running
        /// the (potentially slow) credential-reject leg on a 401. Off by default; enabled via
        /// <see cref="GVFSConstants.GitConfig.ReleaseConnectionBeforeCredentialReject"/>. Overridable
        /// in tests.
        /// </summary>
        protected virtual bool ShouldReleaseConnectionBeforeCredentialReject => releaseConnectionBeforeCredentialReject;

        // Runtime credential fetches (object/pack downloads, incl. background
        // maintenance prefetch) are bounded so a missed/ignored credential prompt
        // can't hang forever. The bound is generous (RetryConfig's 120s default)
        // rather than the 30s default: this same requestor is shared by interactive
        // on-demand hydration and by the user-initiated prefetch/clone verbs, where
        // a human may legitimately take longer than 30s to answer a GCM cold-start /
        // MFA / smartcard prompt. 120s still bounds the hang while being long enough
        // that a noticed prompt is not cut off spuriously. The value comes from the
        // already-loaded RetryConfig so no config read happens per requestor.
        protected virtual int CredentialTimeoutMs => this.RetryConfig.CredentialTimeoutMs;

        public static long GetNewRequestId()
        {
            return Interlocked.Increment(ref requestCount);
        }

        public void Dispose()
        {
            if (this.client != null)
            {
                this.client.Dispose();
                this.client = null;
            }
        }

        protected GitEndPointResponseData SendRequest(
            long requestId,
            Uri requestUri,
            HttpMethod httpMethod,
            string requestContent,
            CancellationToken cancellationToken,
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            string authString = null;
            string errorMessage;
            if (!this.authentication.IsAnonymous &&
                !this.authentication.TryGetCredentials(this.Tracer, out authString, out errorMessage, out bool credentialFetchTimedOut, this.CredentialTimeoutMs, cancellationToken))
            {
                return new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    new GitObjectsHttpException(HttpStatusCode.Unauthorized, errorMessage),

                    // A timed-out credential fetch means nobody answered the prompt. Retrying
                    // immediately just spawns another prompt and burns the retry budget on a
                    // human-response bound (up to MaxAttempts x CredentialTimeoutMs), so give up
                    // and let backoff decide when another attempt is worthwhile.
                    shouldRetry: !credentialFetchTimedOut,
                    message: null,
                    onResponseDisposed: null);
            }

            HttpRequestMessage request = new HttpRequestMessage(httpMethod, requestUri);

            // By default, VSTS auth failures result in redirects to SPS to reauthenticate.
            // To provide more consistent behavior when using the GCM, have them send us 401s instead
            request.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");

            request.Headers.UserAgent.Add(this.userAgentHeader);

            if (!this.authentication.IsAnonymous)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);
            }

            if (acceptType != null)
            {
                request.Headers.Accept.Add(acceptType);
            }

            if (requestContent != null)
            {
                request.Content = new StringContent(requestContent, Encoding.UTF8, "application/json");
            }

            EventMetadata responseMetadata = new EventMetadata();
            responseMetadata.Add("RequestId", requestId);
            responseMetadata.Add("availableConnections", availableConnections.CurrentCount);

            Stopwatch requestStopwatch = Stopwatch.StartNew();

            if (!availableConnections.Wait(ConnectionPoolWaitTimeoutMs, cancellationToken))
            {
                TimeSpan connectionWaitTime = requestStopwatch.Elapsed;
                responseMetadata.Add("connectionWaitTimeMS", $"{connectionWaitTime.TotalMilliseconds:F4}");
                this.Tracer.RelatedWarning(responseMetadata, "SendRequest: Connection pool exhausted, all connections busy");

                return new GitEndPointResponseData(
                    HttpStatusCode.ServiceUnavailable,
                    new GitObjectsHttpException(HttpStatusCode.ServiceUnavailable, "Connection pool exhausted - all connections busy"),
                    shouldRetry: true,
                    message: null,
                    onResponseDisposed: null);
            }

            TimeSpan connectionWaitTimeElapsed = requestStopwatch.Elapsed;
            if (connectionWaitTimeElapsed.TotalMilliseconds > ConnectionPoolContentionThresholdMs)
            {
                EventMetadata contentionMetadata = new EventMetadata();
                contentionMetadata.Add("RequestId", requestId);
                contentionMetadata.Add("availableConnections", availableConnections.CurrentCount);
                contentionMetadata.Add("connectionWaitTimeMS", $"{connectionWaitTimeElapsed.TotalMilliseconds:F4}");
                this.Tracer.RelatedWarning(contentionMetadata, "SendRequest: Connection pool contention detected");
            }

            TimeSpan responseWaitTime = default(TimeSpan);
            GitEndPointResponseData gitEndPointResponseData = null;
            HttpResponseMessage response = null;

            // Tracks whether we already released the connection-pool slot inside the try body
            // (F06 early-release before the credential-reject leg). The finally block must not
            // release a second time, which would corrupt the semaphore's permit count.
            bool connectionReleasedEarly = false;

            try
            {
                requestStopwatch.Restart();

                try
                {
                    response = this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();
                }
                catch (HttpRequestException httpRequestException) when (TryGetResponseMessageFromHttpRequestException(httpRequestException, request, out response))
                {
                    /* HttpClientHandler may automatically resubmit in certain circumstances, such as a 401 unauthorized response.
                     * This resubmit can throw (instead of returning a proper status code) in some cases, such
                     * as when there is an exception loading the default credentials.
                     * If we can extract the original response message from the exception, we can continue and process the original failed status code. */
                    Tracer.RelatedWarning(responseMetadata, $"An exception occurred while resubmitting the request, but the original response is available.");
                }
                finally
                {
                    responseWaitTime = requestStopwatch.Elapsed;
                }

                responseMetadata.Add("CacheName", GetSingleHeaderOrEmpty(response.Headers, "X-Cache-Name"));
                responseMetadata.Add("StatusCode", response.StatusCode);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string contentType = GetSingleHeaderOrEmpty(response.Content.Headers, "Content-Type");
                    responseMetadata.Add("ContentType", contentType);

                    if (!this.authentication.IsAnonymous)
                    {
                        this.authentication.ApproveCredentials(this.Tracer, authString, this.CredentialTimeoutMs, cancellationToken);
                    }

                    Stream responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

                    gitEndPointResponseData = new GitEndPointResponseData(
                        response.StatusCode,
                        contentType,
                        responseStream,
                        message: response,
                        onResponseDisposed: () => availableConnections.Release());
                }
                else
                {
                    errorMessage = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    HttpStatusCode statusCode = response.StatusCode;
                    int statusInt = (int)statusCode;

                    bool shouldRetry = ShouldRetry(statusCode);

                    if (statusCode == HttpStatusCode.Unauthorized &&
                        this.authentication.IsAnonymous)
                    {
                        shouldRetry = false;
                        errorMessage = "Anonymous request was rejected with a 401";
                    }
                    else if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.BadRequest || statusCode == HttpStatusCode.Redirect)
                    {
                        if (this.ShouldReleaseConnectionBeforeCredentialReject)
                        {
                            // F06: the error body is already buffered into errorMessage, and the
                            // reject leg can block for a long time on a slow or hung credential
                            // helper. Free the process-wide connection slot before that wait so
                            // healthy parallel requests are not starved by credential contention.
                            // The finally block honors connectionReleasedEarly so a reject that
                            // throws (e.g. on cancellation) does not double-release the permit.
                            response.Dispose();
                            response = null;
                            availableConnections.Release();
                            connectionReleasedEarly = true;
                        }

                        this.authentication.RejectCredentials(this.Tracer, authString, this.CredentialTimeoutMs, cancellationToken);
                        if (!this.authentication.IsBackingOff)
                        {
                            errorMessage = string.Format("Server returned error code {0} ({1}). Your PAT may be expired and we are asking for a new one. Original error message from server: {2}", statusInt, statusCode, errorMessage);
                        }
                        else
                        {
                            errorMessage = string.Format("Server returned error code {0} ({1}) after successfully renewing your PAT. You may not have access to this repo. Original error message from server: {2}", statusInt, statusCode, errorMessage);
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("Server returned error code {0} ({1}). Original error message from server: {2}", statusInt, statusCode, errorMessage);
                    }

                    gitEndPointResponseData = new GitEndPointResponseData(
                        statusCode,
                        new GitObjectsHttpException(statusCode, errorMessage),
                        shouldRetry,
                        message: connectionReleasedEarly ? null : response,
                        onResponseDisposed: connectionReleasedEarly ? (Action)null : () => availableConnections.Release());
                }
            }
            catch (TaskCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                errorMessage = string.Format("Request to {0} timed out", requestUri);

                gitEndPointResponseData = new GitEndPointResponseData(
                    HttpStatusCode.RequestTimeout,
                    new GitObjectsHttpException(HttpStatusCode.RequestTimeout, errorMessage),
                    shouldRetry: true,
                    message: response,
                    onResponseDisposed: () => availableConnections.Release());
            }
            catch (HttpRequestException httpRequestException) when (httpRequestException.InnerException is System.Security.Authentication.AuthenticationException)
            {
                // This exception is thrown on OSX, when user declines to give permission to access certificate
                gitEndPointResponseData = new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    httpRequestException.InnerException,
                    shouldRetry: false,
                    message: response,
                    onResponseDisposed: () => availableConnections.Release());
            }
            catch (WebException ex)
            {
                gitEndPointResponseData = new GitEndPointResponseData(
                    HttpStatusCode.InternalServerError,
                    ex,
                    shouldRetry: true,
                    message: response,
                    onResponseDisposed: () => availableConnections.Release());
            }
            finally
            {
                responseMetadata.Add("connectionWaitTimeMS", $"{connectionWaitTimeElapsed.TotalMilliseconds:F4}");
                responseMetadata.Add("responseWaitTimeMS", $"{responseWaitTime.TotalMilliseconds:F4}");

                this.Tracer.RelatedEvent(EventLevel.Informational, "NetworkResponse", responseMetadata);

                if (gitEndPointResponseData == null)
                {
                    // If gitEndPointResponseData is null there was an unhandled exception
                    if (response != null)
                    {
                        response.Dispose();
                    }

                    // Don't release a second time if the connection slot was already freed
                    // early (F06) before a reject leg that then threw (e.g. on cancellation).
                    if (!connectionReleasedEarly)
                    {
                        availableConnections.Release();
                    }
                }
            }

            return gitEndPointResponseData;
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            // Retry timeout, Unauthorized, 429 (Too Many Requests), and 5xx errors
            int statusInt = (int)statusCode;
            if (statusCode == HttpStatusCode.RequestTimeout ||
                statusCode == HttpStatusCode.Unauthorized ||
                statusInt == 429 ||
                (statusInt >= 500 && statusInt < 600))
            {
                return true;
            }

            return false;
        }

        private static string GetSingleHeaderOrEmpty(HttpHeaders headers, string headerName)
        {
            IEnumerable<string> values;
            if (headers.TryGetValues(headerName, out values))
            {
                return values.First();
            }

            return string.Empty;
        }

        /// <summary>
        /// This method is based on a private method System.Net.Http.HttpClientHandler.CreateResponseMessage
        /// </summary>
        private static bool TryGetResponseMessageFromHttpRequestException(HttpRequestException httpRequestException, HttpRequestMessage request, out HttpResponseMessage httpResponseMessage)
        {
            var webResponse = (httpRequestException?.InnerException as WebException)?.Response as HttpWebResponse;
            if (webResponse == null)
            {
                httpResponseMessage = null;
                return false;
            }

            httpResponseMessage = new HttpResponseMessage(webResponse.StatusCode);
            httpResponseMessage.ReasonPhrase = webResponse.StatusDescription;
            httpResponseMessage.Version = webResponse.ProtocolVersion;
            httpResponseMessage.RequestMessage = request;
            httpResponseMessage.Content = new StreamContent(webResponse.GetResponseStream());
            request.RequestUri = webResponse.ResponseUri;
            WebHeaderCollection rawHeaders = webResponse.Headers;
            HttpContentHeaders responseContentHeaders = httpResponseMessage.Content.Headers;
            HttpResponseHeaders responseHeaders = httpResponseMessage.Headers;
            if (webResponse.ContentLength >= 0)
            {
                responseContentHeaders.ContentLength = webResponse.ContentLength;
            }

            for (int i = 0; i < rawHeaders.Count; i++)
            {
                string key = rawHeaders.GetKey(i);
                if (string.Compare(key, "Content-Length", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    string[] values = rawHeaders.GetValues(i);
                    if (!responseHeaders.TryAddWithoutValidation(key, values))
                    {
                        bool flag = responseContentHeaders.TryAddWithoutValidation(key, values);
                    }
                }
            }

            return true;

        }

        private static void TryApplyConnectionLimitFromConfig(ITracer tracer, Enlistment enlistment)
        {
            try
            {
                GitProcess.ConfigResult result = enlistment.CreateGitProcess().GetFromConfig(GVFSConstants.GitConfig.MaxHttpConnectionsConfig);
                string error;
                int configuredLimit;
                if (!result.TryParseAsInt(0, 1, out configuredLimit, out error))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("error", error);
                    tracer.RelatedWarning(metadata, "HttpRequestor: Invalid gvfs.max-http-connections config value, using default");
                    return;
                }

                if (configuredLimit > 0)
                {
                    int currentLimit = availableConnections.CurrentCount;

                    // Adjust the existing semaphore rather than replacing it, so any
                    // in-flight waiters release permits to the correct instance.
                    int delta = configuredLimit - currentLimit;
                    if (delta > 0)
                    {
                        for (int i = 0; i < delta; i++)
                        {
                            availableConnections.Release();
                        }
                    }
                    else if (delta < 0)
                    {
                        for (int i = 0; i < -delta; i++)
                        {
                            availableConnections.Wait();
                        }
                    }

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("configuredLimit", configuredLimit);
                    metadata.Add("previousLimit", currentLimit);
                    tracer.RelatedEvent(EventLevel.Informational, "HttpRequestor_ConnectionLimitConfigured", metadata);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedWarning(metadata, "HttpRequestor: Failed to read gvfs.max-http-connections config, using default");
            }
        }

        private static void TryApplyReleaseConnectionBeforeRejectFromConfig(ITracer tracer, Enlistment enlistment)
        {
            try
            {
                GitProcess.ConfigResult result = enlistment.CreateGitProcess().GetFromConfig(GVFSConstants.GitConfig.ReleaseConnectionBeforeCredentialReject);
                if (!result.TryParseAsString(out string value, out string error))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("error", error);
                    tracer.RelatedWarning(metadata, "HttpRequestor: Failed to read gvfs.release-connection-before-credential-reject config, using default");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(value) && IsGitConfigTrue(value))
                {
                    releaseConnectionBeforeCredentialReject = true;

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("value", value);
                    tracer.RelatedEvent(EventLevel.Informational, "HttpRequestor_ReleaseConnectionBeforeCredentialRejectEnabled", metadata);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedWarning(metadata, "HttpRequestor: Failed to read gvfs.release-connection-before-credential-reject config, using default");
            }
        }

        private static bool IsGitConfigTrue(string value)
        {
            // Mirror git's boolean truthiness for config values.
            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
