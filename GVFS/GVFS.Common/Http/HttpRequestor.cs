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
        }

        /// <param name="forceAnonymous">
        /// Sends the request without credentials regardless of the authentication
        /// state. Required by the /gvfs/config probe that DETERMINES whether the
        /// server allows anonymous access: that probe runs before initialization
        /// completes, so it must not call <see cref="GitAuthentication.TryGetCredentials"/>,
        /// which would wait for the initialization this very request is part of.
        /// </param>
        protected GitEndPointResponseData SendRequest(
            long requestId,
            Uri requestUri,
            HttpMethod httpMethod,
            string requestContent,
            CancellationToken cancellationToken,
            MediaTypeWithQualityHeaderValue acceptType = null,
            bool forceAnonymous = false)
        {
            // Resolve the anonymous decision once. Another thread can change
            // IsAnonymous while this request is in flight, and the credential
            // gate, the Authorization header, and the response handling below
            // must all agree on a single value.
            bool sendAnonymous = forceAnonymous || this.authentication.IsAnonymous;

            string authString = null;
            string errorMessage;
            if (!sendAnonymous &&
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

            if (!sendAnonymous)
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

                    if (!sendAnonymous)
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
                        sendAnonymous)
                    {
                        // The request carried no credentials, so there is nothing to
                        // reject. For the initial probe this is the definitive answer
                        // that the server requires authentication.
                        shouldRetry = false;
                        errorMessage = "Anonymous request was rejected with a 401";
                    }
                    else if (ShouldRejectCredentials(response.StatusCode, errorMessage))
                    {
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

                    gitEndPointResponseData = new GitEndPointResponseData(
                        response.StatusCode,
                        new GitObjectsHttpException(response.StatusCode, errorMessage),
                        shouldRetry,
                        message: response,
                        onResponseDisposed: () => availableConnections.Release());
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
        /// The message the Azure DevOps GVFS cache server returns in a 400 (Bad Request)
        /// body when the request carried no parseable Basic Authorization header - i.e.
        /// the one 400 that genuinely means "authentication required".
        /// </summary>
        /// <remarks>
        /// Mirrors the cache server's own message, emitted by
        /// GvfsHttpHandler.PrepareContextAsync as
        /// $"A valid {scheme} {header} header is required." with scheme="Basic" and
        /// header="Authorization". Kept as a literal (not a format) so a substring match
        /// stays robust if the server text is wrapped or prefixed.
        /// </remarks>
        internal const string CacheServerAuthRequiredBadRequestMessage = "A valid Basic Authorization header is required.";

        /// <summary>
        /// Determines whether an HTTP response indicates an authentication failure
        /// that warrants rejecting (erasing) the stored credential.
        /// </summary>
        /// <remarks>
        /// 401 (Unauthorized) and 302 (Redirect to the Azure DevOps sign-in page) are
        /// always genuine authentication failures. A 400 (Bad Request) is usually NOT an
        /// auth failure - a present-but-expired/invalid credential returns 401, and a
        /// malformed request (e.g. a corrupt object SHA in the loose-object URL) returns a
        /// 400 that has nothing to do with credentials. Rejecting credentials on every 400
        /// erased valid credentials and caused a storm of credential-manager popups.
        ///
        /// The one exception: the GVFS cache server returns a 400 (instead of a 401) when
        /// the request carried no parseable Basic Authorization header. That single 400 is
        /// genuinely "authentication required", and microsoft/git's git-gvfs-helper maps it
        /// to a 401 for the same reason (its normalize step notes the cache server "sends a
        /// somewhat bogus 400 instead of the normal 401 when AUTH is required", and its TODO
        /// asks to confirm the response body - which is exactly what we do here). We only
        /// treat a 400 as an auth failure when the body matches that specific message.
        /// </remarks>
        internal static bool ShouldRejectCredentials(HttpStatusCode statusCode, string responseBody)
        {
            if (statusCode == HttpStatusCode.Unauthorized ||
                statusCode == HttpStatusCode.Redirect)
            {
                return true;
            }

            if (statusCode == HttpStatusCode.BadRequest &&
                responseBody != null &&
                responseBody.IndexOf(CacheServerAuthRequiredBadRequestMessage, StringComparison.OrdinalIgnoreCase) >= 0)
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
    }
}
