using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
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
        private static long requestCount = 0;
        private static SemaphoreSlim availableConnections;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private HttpClient client;
        private GitAuthentication authentication;

        static HttpRequestor()
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount;
            availableConnections = new SemaphoreSlim(ServicePointManager.DefaultConnectionLimit);
        }

        public HttpRequestor(ITracer tracer, RetryConfig retryConfig, GitAuthentication authentication)
        {
            this.client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            this.client.Timeout = retryConfig.Timeout;
            this.RetryConfig = retryConfig;
            this.authentication = authentication;

            this.Tracer = tracer;

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
            string authString;
            string errorMessage;
            if (!this.authentication.TryGetCredentials(this.Tracer, out authString, out errorMessage))
            {
                return new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    new GitObjectsHttpException(HttpStatusCode.Unauthorized, errorMessage),
                    shouldRetry: true);
            }

            HttpRequestMessage request = new HttpRequestMessage(httpMethod, requestUri);
            request.Headers.UserAgent.Add(this.userAgentHeader);

            if (!string.IsNullOrEmpty(authString))
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

            availableConnections.Wait(cancellationToken);

            try
            {
                HttpResponseMessage response = this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();

                responseMetadata.Add("CacheName", GetSingleHeaderOrEmpty(response.Headers, "X-Cache-Name"));
                responseMetadata.Add("StatusCode", response.StatusCode);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string contentType = GetSingleHeaderOrEmpty(response.Content.Headers, "Content-Type");
                    responseMetadata.Add("ContentType", contentType);

                    this.authentication.ConfirmCredentialsWorked(authString);
                    Stream responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    return new GitEndPointResponseData(response.StatusCode, contentType, responseStream);
                }
                else
                {
                    errorMessage = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    int statusInt = (int)response.StatusCode;

                    if (string.IsNullOrWhiteSpace(errorMessage))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            this.authentication.Revoke(authString);
                            if (!this.authentication.IsBackingOff)
                            {
                                errorMessage = "Server returned error code 401 (Unauthorized). Your PAT may be expired and we are asking for a new one.";
                            }
                            else
                            {
                                errorMessage = "Server returned error code 401 (Unauthorized) after successfully renewing your PAT. You may not have access to this repo";
                            }
                        }
                        else
                        {
                            errorMessage = string.Format("Server returned error code {0} ({1})", statusInt, response.StatusCode);
                        }
                    }

                    return new GitEndPointResponseData(response.StatusCode, new GitObjectsHttpException(response.StatusCode, errorMessage), ShouldRetry(response.StatusCode));
                }
            }
            catch (TaskCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                errorMessage = string.Format("Request to {0} timed out", requestUri);
                return new GitEndPointResponseData(HttpStatusCode.RequestTimeout, new GitObjectsHttpException(HttpStatusCode.RequestTimeout, errorMessage), shouldRetry: true);
            }
            catch (WebException ex)
            {
                return new GitEndPointResponseData(HttpStatusCode.InternalServerError, ex, shouldRetry: true);
            }
            finally
            {
                this.Tracer.RelatedEvent(EventLevel.Informational, "NetworkResponse", responseMetadata);
                availableConnections.Release();
            }
        }
        
        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            // Retry timeout, Unauthorized, and 5xx errors
            int statusInt = (int)statusCode;
            if (statusCode == HttpStatusCode.RequestTimeout ||
                statusCode == HttpStatusCode.Unauthorized ||
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
    }
}
