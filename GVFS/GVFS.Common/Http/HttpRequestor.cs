using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Http
{
    public abstract class HttpRequestor : IDisposable
    {
        public const int DefaultMaxRetries = 5;
        private const int HttpTimeoutMinutes = 10;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private HttpClient client;
        private GitAuthentication authentication;
        
        public HttpRequestor(ITracer tracer, GitAuthentication authentication, int maxConnections)
        {
            this.client = new HttpClient();
            this.client.Timeout = TimeSpan.FromMinutes(HttpTimeoutMinutes);
            this.authentication = authentication;

            this.Tracer = tracer;

            ServicePointManager.DefaultConnectionLimit = maxConnections;

            this.userAgentHeader = new ProductInfoHeaderValue(ProcessHelper.GetEntryClassName(), ProcessHelper.GetCurrentProcessVersion());
        }

        protected ITracer Tracer { get; }

        public void Dispose()
        {
            if (this.client != null)
            {
                this.client.Dispose();
                this.client = null;
            }
        }

        protected GitEndPointResponseData SendRequest(
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

            try
            {
                HttpResponseMessage response = this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string contentType = string.Empty;
                    IEnumerable<string> values;
                    if (response.Content.Headers.TryGetValues("Content-Type", out values))
                    {
                        contentType = values.First();
                    }

                    this.authentication.ConfirmCredentialsWorked();
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
                            if (this.authentication.RevokeAndCheckCanRetry())
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
    }
}
