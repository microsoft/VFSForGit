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

        // SKETCH (design proposal): the credential probe is a diagnostic side-request; keep
        // it short so a confirmed-bad credential is not delayed by a slow probe.
        private static readonly TimeSpan CredentialProbeTimeout = TimeSpan.FromSeconds(15);

        // SKETCH (design proposal): how long a probe result is reused for the same credential.
        // Single-flighting plus this short cache stops a burst of concurrent 400s from fanning
        // out into a burst of probes (and RejectCredentials calls).
        private static readonly TimeSpan CredentialProbeResultTtl = TimeSpan.FromSeconds(30);

        private static long requestCount = 0;
        private static SemaphoreSlim availableConnections;
        private static int connectionLimitConfigured = 0;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private readonly GitAuthentication authentication;

        private HttpClient client;

        // SKETCH (design proposal): a separate client for the credential probe. The probe must
        // OBSERVE a 302 sign-in redirect as its auth-failure signal, so unlike the main client it
        // must NOT auto-follow redirects. Following a same-host redirect would also re-send the
        // Basic auth header to an unintended endpoint. SSL config is applied identically.
        private HttpClient probeClient;

        // SKETCH (design proposal): single-flight + short-lived memoization of the probe result,
        // keyed by the credential that was probed. Guards against a concurrent-400 probe/reject herd.
        private readonly object credentialProbeLock = new object();
        private string lastProbedAuthString;
        private bool lastProbeRejectResult;
        private DateTime lastProbeTimeUtc = DateTime.MinValue;

        static HttpRequestor()
        {
            // HTTP downloads are I/O-bound, not CPU-bound, so we default to
            // 2x ProcessorCount. Can be overridden via gvfs.max-http-connections.
            int connectionLimit = 2 * Environment.ProcessorCount;
            availableConnections = new SemaphoreSlim(connectionLimit);
        }

        protected HttpRequestor(ITracer tracer, RetryConfig retryConfig, Enlistment enlistment)
        {
            this.RetryConfig = retryConfig;

            this.authentication = enlistment.Authentication;

            this.Tracer = tracer;

            // On first instantiation, check git config for a custom connection limit.
            // This runs before any requests are made (during mount initialization).
            if (Interlocked.CompareExchange(ref connectionLimitConfigured, 1, 0) == 0)
            {
                TryApplyConnectionLimitFromConfig(tracer, enlistment);
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
            SocketsHttpHandler handler = new SocketsHttpHandler()
            {
                MaxConnectionsPerServer = Environment.ProcessorCount,
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            };

            this.authentication.ConfigureSocketsHandlerSslIfNeeded(this.Tracer, handler, enlistment.CreateGitProcess());

            this.client = new HttpClient(handler)
            {
                Timeout = retryConfig.Timeout,
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            // SKETCH (design proposal): dedicated probe client with redirects disabled so the
            // probe can see a 302 instead of silently following it to a 200 sign-in page.
            SocketsHttpHandler probeHandler = new SocketsHttpHandler()
            {
                MaxConnectionsPerServer = Environment.ProcessorCount,
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = false,
            };

            this.authentication.ConfigureSocketsHandlerSslIfNeeded(this.Tracer, probeHandler, enlistment.CreateGitProcess());

            this.probeClient = new HttpClient(probeHandler)
            {
                Timeout = retryConfig.Timeout,
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            this.userAgentHeader = new ProductInfoHeaderValue(ProcessHelper.GetEntryClassName(), ProcessHelper.GetCurrentProcessVersion());
        }

        public RetryConfig RetryConfig { get; }

        protected ITracer Tracer { get; }

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

            if (this.probeClient != null)
            {
                this.probeClient.Dispose();
                this.probeClient = null;
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
                !this.authentication.TryGetCredentials(this.Tracer, out authString, out errorMessage))
            {
                return new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    new GitObjectsHttpException(HttpStatusCode.Unauthorized, errorMessage),
                    shouldRetry: true,
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

            // SKETCH (design proposal): tracks whether the credential probe already released the
            // logical connection slot, so the response-disposed handler does not double-release.
            bool connectionSlotReleased = false;

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
                        this.authentication.ApproveCredentials(this.Tracer, authString);
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
                    int statusInt = (int)response.StatusCode;

                    bool shouldRetry = ShouldRetry(response.StatusCode);

                    if (response.StatusCode == HttpStatusCode.Unauthorized &&
                        this.authentication.IsAnonymous)
                    {
                        shouldRetry = false;
                        errorMessage = "Anonymous request was rejected with a 401";
                    }
                    else
                    {
                        // SKETCH (design proposal): a bare 400 is normally a request/formatting
                        // problem, NOT an expired credential (an expired/invalid credential
                        // returns 401 or 302). The one 400 that can mean "no credential reached
                        // the server" is the missing Basic-auth-header case. Before erasing a
                        // possibly-good credential we re-send the SAME credential to a known-good,
                        // auth-enforced endpoint; only a probe that ALSO fails auth (401/302)
                        // proves a real credential failure.
                        bool badRequestConfirmedByProbe = false;
                        if (response.StatusCode == HttpStatusCode.BadRequest &&
                            !this.authentication.IsAnonymous)
                        {
                            // Free the logical connection slot BEFORE the (up-to-15s) probe: the
                            // error body has already been fully read, so the outer request no
                            // longer needs the slot, and the probe uses its own HttpClient/handler
                            // so it does not contend for this pool. This prevents a burst of 400s
                            // from pinning every slot for the probe duration and starving others.
                            availableConnections.Release();
                            connectionSlotReleased = true;

                            badRequestConfirmedByProbe =
                                this.CredentialProbeConfirmsAuthFailure(requestId, requestUri, authString, cancellationToken);
                        }

                        if (ShouldRejectCredentials(response.StatusCode) || badRequestConfirmedByProbe)
                        {
                            // A probe-confirmed 400 is a real auth failure, so it must join the
                            // same reject-and-retry contract as a 401: reject the credential AND
                            // allow a retry so the caller re-authenticates. A bare 400 stays
                            // non-retryable (ShouldRetry is false for it) - only the confirmed
                            // case opts back into retry, otherwise the reject is a useless erase.
                            if (badRequestConfirmedByProbe)
                            {
                                shouldRetry = true;
                            }

                            this.authentication.RejectCredentials(this.Tracer, authString);
                            if (!this.authentication.IsBackingOff)
                            {
                                errorMessage = string.Format("Server returned error code {0} ({1}). Your PAT may be expired and we are asking for a new one. Original error message from server: {2}", statusInt, response.StatusCode, errorMessage);
                            }
                            else
                            {
                                errorMessage = string.Format("Server returned error code {0} ({1}) after successfully renewing your PAT. You may not have access to this repo. Original error message from server: {2}", statusInt, response.StatusCode, errorMessage);
                            }
                        }
                        else
                        {
                            errorMessage = string.Format("Server returned error code {0} ({1}). Original error message from server: {2}", statusInt, response.StatusCode, errorMessage);
                        }
                    }

                    gitEndPointResponseData = new GitEndPointResponseData(
                        response.StatusCode,
                        new GitObjectsHttpException(response.StatusCode, errorMessage),
                        shouldRetry,
                        message: response,
                        onResponseDisposed: () =>
                        {
                            if (!connectionSlotReleased)
                            {
                                availableConnections.Release();
                            }
                        });
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

                    availableConnections.Release();
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

        /// <summary>
        /// Determines whether an HTTP status code indicates an authentication failure
        /// that warrants rejecting (erasing) the stored credential.
        /// </summary>
        /// <remarks>
        /// Only 401 (Unauthorized) and 302 (Redirect to the Azure DevOps sign-in page)
        /// are genuine authentication failures. A 400 (Bad Request) is a request/formatting
        /// problem, NOT an expired credential - an expired or invalid credential always
        /// returns 401 or 302.
        /// </remarks>
        internal static bool ShouldRejectCredentials(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Unauthorized ||
                   statusCode == HttpStatusCode.Redirect;
        }

        /// <summary>
        /// SKETCH (design proposal). Returns the URI of a lightweight, auth-enforced,
        /// guaranteed-well-formed endpoint that can be used to confirm whether the current
        /// credential is still valid. Returns null when this requestor cannot probe (the
        /// caller then treats an ambiguous 400 conservatively and does NOT reject).
        /// </summary>
        /// <param name="failedRequestUri">The URI of the request that returned the 400, so the
        /// probe can target the SAME host (cache vs origin) and exercise the same auth path.</param>
        /// <remarks>
        /// The probe URI MUST be built from a constant we control - never from the request
        /// input that produced the 400 (that input may be the corrupt value that caused it).
        /// Derived requestors override this to point at a known-good object on the same host
        /// that returned the 400.
        /// </remarks>
        protected virtual Uri GetCredentialProbeUri(Uri failedRequestUri)
        {
            return null;
        }

        /// <summary>
        /// SKETCH (design proposal). Re-sends the SAME credential to the known-good probe
        /// endpoint to decide whether a 400 actually reflects a bad credential. Single-flighted
        /// and memoized per credential for a short TTL so concurrent 400s do not fan out.
        /// </summary>
        /// <returns>
        /// true only when the probe itself fails authentication (401/302) - i.e. the
        /// credential really is bad and should be rejected. false when the probe succeeds,
        /// returns any non-auth status (e.g. 200/404 - both prove auth passed), or cannot
        /// run (no probe URI / transport error). The decisive signal is "did the probe get
        /// past auth", so ANY response other than 401/302 means the credential is good.
        /// </returns>
        internal bool CredentialProbeConfirmsAuthFailure(long requestId, Uri failedRequestUri, string authString, CancellationToken cancellationToken)
        {
            lock (this.credentialProbeLock)
            {
                if (this.lastProbedAuthString == authString &&
                    DateTime.UtcNow - this.lastProbeTimeUtc < CredentialProbeResultTtl)
                {
                    // Reuse the recent result for this exact credential (single-flight/memoize).
                    return this.lastProbeRejectResult;
                }

                bool reject = this.RunCredentialProbe(requestId, failedRequestUri, authString, cancellationToken);

                this.lastProbedAuthString = authString;
                this.lastProbeRejectResult = reject;
                this.lastProbeTimeUtc = DateTime.UtcNow;
                return reject;
            }
        }

        private bool RunCredentialProbe(long requestId, Uri failedRequestUri, string authString, CancellationToken cancellationToken)
        {
            Uri probeUri = this.GetCredentialProbeUri(failedRequestUri);
            if (probeUri == null)
            {
                // Cannot probe - be conservative and do NOT reject a possibly-good credential.
                return false;
            }

            Stopwatch probeStopwatch = Stopwatch.StartNew();
            bool probed = this.TryProbeCredential(probeUri, authString, cancellationToken, out HttpStatusCode probeStatus);
            TimeSpan probeElapsed = probeStopwatch.Elapsed;

            if (!probed)
            {
                // Transport failure probing - inconclusive, so do NOT reject.
                return false;
            }

            bool reject = ShouldRejectCredentials(probeStatus);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "Authentication");
            metadata.Add("RequestId", requestId);
            metadata.Add(nameof(probeUri), probeUri.ToString());
            metadata.Add(nameof(probeStatus), probeStatus.ToString());
            metadata.Add("probeElapsedMS", $"{probeElapsed.TotalMilliseconds:F4}");
            metadata.Add("rejectCredential", reject);

            // A 200 or a 404 both prove auth passed (a 404 means we reached "object not found"
            // past the auth gate). We deliberately KEEP the credential on any non-401/302 status,
            // erring toward keeping a possibly-good credential over re-triggering a popup storm.
            // Flag genuinely unexpected statuses so an endpoint that masks auth failures behind an
            // unusual code is visible in telemetry rather than silently trusted.
            if (!reject &&
                probeStatus != HttpStatusCode.OK &&
                probeStatus != HttpStatusCode.NotFound)
            {
                metadata.Add("probeStatusAmbiguous", true);
            }

            this.Tracer.RelatedInfo(metadata, "Credential probe after HTTP 400 completed");

            return reject;
        }

        /// <summary>
        /// SKETCH (design proposal). One-shot, no-retry GET to the probe endpoint carrying
        /// the same Basic auth header as the original request. Reads only the status code.
        /// Deliberately separate from <see cref="SendRequest"/> so it never re-enters the
        /// 400/401 handling (no recursion, no retry, no circuit-breaker interaction), and uses
        /// the redirect-disabled probe client so a 302 sign-in redirect is observed, not followed.
        /// Protected virtual as a test seam so the probe decision can be unit-tested without a
        /// live network.
        /// </summary>
        protected virtual bool TryProbeCredential(Uri probeUri, string authString, CancellationToken cancellationToken, out HttpStatusCode probeStatus)
        {
            probeStatus = default(HttpStatusCode);

            try
            {
                using (HttpRequestMessage probe = new HttpRequestMessage(HttpMethod.Get, probeUri))
                {
                    probe.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");
                    probe.Headers.UserAgent.Add(this.userAgentHeader);
                    if (!this.authentication.IsAnonymous)
                    {
                        probe.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);
                    }

                    // Bound the probe by its own timeout AND honor the caller's cancellation so a
                    // cancelled mount/prefetch is not held hostage by the probe.
                    using (CancellationTokenSource timeout = new CancellationTokenSource(CredentialProbeTimeout))
                    using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
                    using (HttpResponseMessage probeResponse = this.probeClient.SendAsync(
                        probe,
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token).GetAwaiter().GetResult())
                    {
                        probeStatus = probeResponse.StatusCode;
                        return true;
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "Authentication");
                metadata.Add(nameof(probeUri), probeUri.ToString());
                metadata.Add("Exception", e.ToString());
                this.Tracer.RelatedWarning(metadata, "Credential probe after HTTP 400 could not complete; treating as inconclusive");
                return false;
            }
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
    }
}
