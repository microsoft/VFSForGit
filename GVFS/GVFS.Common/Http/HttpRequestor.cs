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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Http
{
    public abstract class HttpRequestor : IDisposable
    {
        private static long requestCount = 0;
        private static SemaphoreSlim availableConnections;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private readonly GitAuthentication authentication;

        private HttpClient client;

        static HttpRequestor()
        {
            /* If machine.config is locked, then initializing ServicePointManager will fail and be unrecoverable.
             * Machine.config locking is typically very brief (~1ms by the antivirus scanner) so we can attempt to lock
             * it ourselves (by opening it for read) *beforehand and briefly wait if it's locked */
            using (var machineConfigLock = GetMachineConfigLock())
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount;
                availableConnections = new SemaphoreSlim(ServicePointManager.DefaultConnectionLimit);
            }
        }

        protected HttpRequestor(ITracer tracer, RetryConfig retryConfig, Enlistment enlistment)
        {
            this.RetryConfig = retryConfig;

            this.authentication = enlistment.Authentication;

            this.Tracer = tracer;

            HttpClientHandler httpClientHandler = new HttpClientHandler() { UseDefaultCredentials = true };

            this.authentication.ConfigureHttpClientHandlerSslIfNeeded(this.Tracer, httpClientHandler, enlistment.CreateGitProcess());

            this.client = new HttpClient(httpClientHandler)
            {
                Timeout = retryConfig.Timeout
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
            availableConnections.Wait(cancellationToken);
            TimeSpan connectionWaitTime = requestStopwatch.Elapsed;

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
                    /* HttpClientHandler will automatically resubmit in certain circumstances, such as a 401 unauthorized response when UseDefaultCredentials
                     * is true but another credential was provided. This resubmit can throw (instead of returning a proper status code) in some case cases, such
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
                    else if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Redirect)
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
                responseMetadata.Add("connectionWaitTimeMS", $"{connectionWaitTime.TotalMilliseconds:F4}");
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

        private static FileStream GetMachineConfigLock()
        {
            var machineConfigLocation = RuntimeEnvironment.SystemConfigurationFile;
            var tries = 0;
            var maxTries = 3;
            while (tries++ < maxTries)
            {
                try
                {
                    /* Opening with FileShare.Read will fail if another process (eg antivirus) has opened the file for write,
                     but will still let ServicePointManager read the file.*/
                    FileStream stream = File.Open(machineConfigLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return stream;
                }
                catch (IOException e) when ((uint)e.HResult == 0x80070020) // SHARING_VIOLATION
                {
                    Thread.Sleep(10);
                }
            }
            /* Couldn't get the lock - the process will likely fail. */
            return null;
        }
    }
}
