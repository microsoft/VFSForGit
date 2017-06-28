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
        public const int DefaultMaxRetries = 5;
        private const int HttpTimeoutMinutes = 10;

        private static long requestCount = 0;

        private readonly ProductInfoHeaderValue userAgentHeader;
        
        private HttpClient client;
        private GitAuthentication authentication;

        static HttpRequestor()
        {
            ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount;
        }

        public HttpRequestor(ITracer tracer, GitAuthentication authentication)
        {
            this.client = new HttpClient();
            this.client.Timeout = TimeSpan.FromMinutes(HttpTimeoutMinutes);
            this.authentication = authentication;

            this.Tracer = tracer;
            
            this.userAgentHeader = new ProductInfoHeaderValue(ProcessHelper.GetEntryClassName(), ProcessHelper.GetCurrentProcessVersion());
        }

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
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            string authString;
            string errorMessage;
            if (!this.authentication.TryGetCredentials(this.Tracer, out authString, out errorMessage))
            {
                return new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    new GitObjectsHttpException(HttpStatusCode.Unauthorized, errorMessage),
                    shouldRetry: false);
            }

            HttpRequestMessage request = new HttpRequestMessage(httpMethod, requestUri);
            request.Headers.UserAgent.Add(this.userAgentHeader);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

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

            try
            {
                HttpResponseMessage response = this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                
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
                            if (this.authentication.RevokeAndCheckCanRetry(authString))
                            {
                                return new GitEndPointResponseData(
                                    response.StatusCode,
                                    new GitObjectsHttpException(response.StatusCode, "Server returned error code 401 (Unauthorized). Your PAT may be expired."),
                                    shouldRetry: true);
                            }
                            else
                            {
                                return new GitEndPointResponseData(
                                    response.StatusCode,
                                    new GitObjectsHttpException(response.StatusCode, "Server returned error code 401 (Unauthorized) after successfully renewing your PAT. You may not have access to this repo"),
                                    shouldRetry: false);
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
            }
        }
        
        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            // Retry timeouts and 5xx errors
            int statusInt = (int)statusCode;
            if (statusCode == HttpStatusCode.RequestTimeout ||
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
