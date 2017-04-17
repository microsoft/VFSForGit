using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    public class HttpGitObjects
    {
        private const string AreaPath = "HttpGitObjects";
        private const int HttpTimeoutMinutes = 10;
        private const int DefaultMaxRetries = 5;
        private const int AuthorizationBackoffMinutes = 1;

        private static readonly MediaTypeWithQualityHeaderValue CustomLooseObjectsHeader
            = new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.CustomLooseObjectsMediaType);

        private static HttpClient client;

        private readonly ProductInfoHeaderValue userAgentHeader;

        private Enlistment enlistment;
        private ITracer tracer;

        private DateTime authRetryBackoff = DateTime.MinValue;
        private bool credentialHasBeenRevoked = false;

        private object gitAuthorizationLock = new object();
        private string gitAuthorization;

        static HttpGitObjects()
        {
            client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(HttpTimeoutMinutes);
        }

        public HttpGitObjects(ITracer tracer, Enlistment enlistment, int maxConnections)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.MaxRetries = DefaultMaxRetries;
            ServicePointManager.DefaultConnectionLimit = maxConnections;

            this.userAgentHeader = new ProductInfoHeaderValue(ProcessHelper.GetEntryClassName(), ProcessHelper.GetCurrentProcessVersion());
        }

        public enum ContentType
        {
            None,
            LooseObject,
            BatchedLooseObjects,
            PackFile
        }

        public int MaxRetries { get; set; }

        public bool TryRefreshCredentials()
        {
            return this.TryGetCredentials(out this.gitAuthorization);
        }

        public virtual List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds)
        {
            string objectIdsJson = ToJsonList(objectIds);
            Uri gvfsEndpoint = new Uri(this.enlistment.RepoUrl + "/gvfs/sizes");

            EventMetadata metadata = new EventMetadata();
            int objectIdCount = objectIds.Count();
            if (objectIdCount > 10)
            {
                metadata.Add("ObjectIdCount", objectIdCount);
            }
            else
            {
                metadata.Add("ObjectIdJson", objectIdsJson);
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "QueryFileSizes", metadata, Keywords.Network);

            RetryWrapper<List<GitObjectSize>> retrier = new RetryWrapper<List<GitObjectSize>>(this.MaxRetries);
            retrier.OnFailure += RetryWrapper<List<GitObjectSize>>.StandardErrorHandler(this.tracer, "QueryFileSizes");

            RetryWrapper<List<GitObjectSize>>.InvocationResult requestTask = retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(gvfsEndpoint, HttpMethod.Post, objectIdsJson);
                    if (response.HasErrors)
                    {
                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(response.Error, response.ShouldRetry);
                    }

                    using (StreamReader reader = new StreamReader(response.Stream))
                    {
                        string objectSizesString = reader.RetryableReadToEnd();
                        List<GitObjectSize> objectSizes = JsonConvert.DeserializeObject<List<GitObjectSize>>(objectSizesString);
                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(objectSizes);
                    }
                });

            return requestTask.Result ?? new List<GitObjectSize>(0);
        }

        public GVFSConfigResponse QueryGVFSConfig()
        {
            Uri gvfsConfigEndpoint;
            string gvfsConfigEndpointString = this.enlistment.RepoUrl + GVFSConstants.GVFSConfigEndpointSuffix;
            try
            {
                gvfsConfigEndpoint = new Uri(gvfsConfigEndpointString);
            }
            catch (UriFormatException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.QueryGVFSConfig));
                metadata.Add("ErrorMessage", e);
                metadata.Add("Url", gvfsConfigEndpointString);
                this.tracer.RelatedError(metadata, Keywords.Network);

                return null;
            }

            RetryWrapper<GVFSConfigResponse> retrier = new RetryWrapper<GVFSConfigResponse>(this.MaxRetries);
            retrier.OnFailure += RetryWrapper<GVFSConfigResponse>.StandardErrorHandler(this.tracer, "QueryGvfsConfig");

            RetryWrapper<GVFSConfigResponse>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(gvfsConfigEndpoint, HttpMethod.Get, null);
                    if (response.HasErrors)
                    {
                        return new RetryWrapper<GVFSConfigResponse>.CallbackResult(response.Error, response.ShouldRetry);
                    }
                    
                    try
                    {
                        using (StreamReader reader = new StreamReader(response.Stream))
                        {
                            string configString = reader.RetryableReadToEnd();
                            return new RetryWrapper<GVFSConfigResponse>.CallbackResult(
                                JsonConvert.DeserializeObject<GVFSConfigResponse>(configString));
                        }
                    }
                    catch (JsonReaderException e)
                    {
                        return new RetryWrapper<GVFSConfigResponse>.CallbackResult(e, false);
                    }
                });

            return output.Result;
        }

        public virtual GitRefs QueryInfoRefs(string branch)
        {
            Uri infoRefsEndpoint;
            try
            {
                infoRefsEndpoint = new Uri(this.enlistment.RepoUrl + GVFSConstants.InfoRefsEndpointSuffix);
            }
            catch (UriFormatException)
            {
                return null;
            }

            RetryWrapper<GitRefs> retrier = new RetryWrapper<GitRefs>(this.MaxRetries);
            retrier.OnFailure += RetryWrapper<GitRefs>.StandardErrorHandler(this.tracer, "QueryInfoRefs");

            RetryWrapper<GitRefs>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(infoRefsEndpoint, HttpMethod.Get, null);
                    if (response.HasErrors)
                    {
                        return new RetryWrapper<GitRefs>.CallbackResult(response.Error, response.ShouldRetry);
                    }

                    using (StreamReader reader = new StreamReader(response.Stream))
                    {
                        List<string> infoRefsResponse = reader.RetryableReadAllLines();
                        return new RetryWrapper<GitRefs>.CallbackResult(new GitRefs(infoRefsResponse, branch));
                    }
                });

            return output.Result;
        }

        /// <summary>
        /// Get the <see cref="Uri"/>s to download and store in the pack directory for bootstrapping
        /// </summary>
        public IList<Uri> TryGetBootstrapPackSources(Uri bootstrapSource, string branchName)
        {
            IList<Uri> packUris = null;

            RetryWrapper<GitObjectTaskResult>.InvocationResult output = this.TrySendProtocolRequest(
                onSuccess: (tryCount, response) =>
                {
                    using (StreamReader reader = new StreamReader(response.Stream))
                    {
                        string packUriString = reader.RetryableReadToEnd();
                        packUris = JsonConvert.DeserializeObject<BootstrapResponse>(packUriString).PackUris;
                        return new RetryWrapper<GitObjectTaskResult>.CallbackResult(new GitObjectTaskResult(true));
                    }
                },
                onFailure: RetryWrapper<GitObjectTaskResult>.StandardErrorHandler(this.tracer, nameof(this.TryGetBootstrapPackSources)),
                method: HttpMethod.Post,
                endPoint: bootstrapSource,
                requestBody: JsonConvert.SerializeObject(new { branchName = branchName }));

            return packUris;
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadLooseObject(
            string objectId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ObjectId", objectId);

            this.tracer.RelatedEvent(EventLevel.Informational, "DownloadLooseObject", metadata, Keywords.Network);

            return this.TrySendProtocolRequest(
                onSuccess,
                onFailure,
                HttpMethod.Get,
                new Uri(this.enlistment.ObjectsEndpointUrl + "/" + objectId));
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            Func<IEnumerable<string>> objectIdGenerator,
            int commitDepth,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            // We pass the query generator in as a function because we don't want the consumer to know about JSON or network retry logic,
            // but we still want the consumer to be able to change the query on each retry if we fail during their onSuccess handler.
            return this.TrySendProtocolRequest(
                onSuccess,
                onFailure,
                HttpMethod.Post,
                new Uri(this.enlistment.ObjectsEndpointUrl),
                () => this.ObjectIdsJsonGenerator(objectIdGenerator, commitDepth),
                preferBatchedLooseObjects ? CustomLooseObjectsHeader : null);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            IEnumerable<string> objectIds,
            int commitDepth,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            string objectIdsJson = CreateObjectIdJson(objectIds, commitDepth);
            int objectCount = objectIds.Count();
            EventMetadata metadata = new EventMetadata();
            if (objectCount < 10)
            {
                metadata.Add("ObjectIds", string.Join(", ", objectIds));
            }
            else
            {
                metadata.Add("ObjectIdCount", objectCount);
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "DownloadObjects", metadata, Keywords.Network);

            return this.TrySendProtocolRequest(
                onSuccess,
                onFailure,
                HttpMethod.Post,
                new Uri(this.enlistment.ObjectsEndpointUrl),
                objectIdsJson,
                preferBatchedLooseObjects ? CustomLooseObjectsHeader : null);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            HttpMethod method,
            Uri endPoint,
            string requestBody = null,
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            return this.TrySendProtocolRequest(
                onSuccess,
                onFailure,
                method,
                endPoint,
                () => requestBody,
                acceptType);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            HttpMethod method,
            Uri endPoint,
            Func<string> requestBodyGenerator,
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            return this.TrySendProtocolRequest(
                onSuccess,
                onFailure,
                method,
                () => endPoint,
                requestBodyGenerator,
                acceptType);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
           Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
           Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
           HttpMethod method,
           Func<Uri> endPointGenerator,
           Func<string> requestBodyGenerator,
           MediaTypeWithQualityHeaderValue acceptType = null)
        {
            RetryWrapper<GitObjectTaskResult> retrier = new RetryWrapper<GitObjectTaskResult>(this.MaxRetries);
            if (onFailure != null)
            {
                retrier.OnFailure += onFailure;
            }

            return retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(
                        endPointGenerator(),
                        method,
                        requestBodyGenerator(),
                        acceptType);
                    if (response.HasErrors)
                    {
                        return new RetryWrapper<GitObjectTaskResult>.CallbackResult(response.Error, response.ShouldRetry, new GitObjectTaskResult(response.StatusCode));
                    }

                    using (Stream responseStream = response.Stream)
                    {
                        return onSuccess(tryCount, response);
                    }
                });
        }

        private static string ToJsonList(IEnumerable<string> strings)
        {
            return "[\"" + string.Join("\",\"", strings) + "\"]";
        }

        private static string CreateObjectIdJson(IEnumerable<string> strings, int commitDepth)
        {
            return "{\"commitDepth\": " + commitDepth + ", \"objectIds\":" + ToJsonList(strings) + "}";
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

        private bool TryGetCredentials(out string authString)
        {
            authString = this.gitAuthorization;
            if (authString == null)
            {
                lock (this.gitAuthorizationLock)
                {
                    if (this.gitAuthorization == null)
                    {
                        string gitUsername;
                        string gitPassword;

                        // These auth settings are necessary to support running the functional tests on build servers.
                        // The reason it's needed is that the GVFS.Service runs as LocalSystem, and the build agent does not
                        // so storing the agent's creds in the Windows Credential Store does not allow the service to discover it
                        GitProcess git = new GitProcess(this.enlistment);
                        GitProcess.Result usernameResult = git.GetFromConfig("gvfs.FunctionalTests.UserName");
                        GitProcess.Result passwordResult = git.GetFromConfig("gvfs.FunctionalTests.Password");

                        if (!usernameResult.HasErrors &&
                            !passwordResult.HasErrors)
                        {
                            gitUsername = usernameResult.Output.TrimEnd('\n');
                            gitPassword = passwordResult.Output.TrimEnd('\n');

                            EventMetadata metadata = new EventMetadata()
                            {
                                { "username", gitUsername },
                            };

                            this.tracer.RelatedEvent(EventLevel.LogAlways, "FunctionalTestCreds", metadata);
                        }
                        else
                        {
                            bool backingOff = DateTime.Now < this.authRetryBackoff;
                            if (this.credentialHasBeenRevoked)
                            {
                                // Update backoff after an immediate first retry.
                                this.authRetryBackoff = DateTime.Now.AddMinutes(AuthorizationBackoffMinutes);
                            }

                            if (backingOff ||
                                !GitProcess.TryGetCredentials(this.tracer, this.enlistment, out gitUsername, out gitPassword))
                            {
                                authString = null;
                                return false;
                            }
                        }

                        this.gitAuthorization = Convert.ToBase64String(Encoding.ASCII.GetBytes(gitUsername + ":" + gitPassword));
                    }

                    authString = this.gitAuthorization;
                }
            }

            return true;
        }

        private GitEndPointResponseData SendRequest(
            Uri requestUri,
            HttpMethod httpMethod,
            string requestContent,
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            string authString;
            if (!this.TryGetCredentials(out authString))
            {
                string message =
                    this.authRetryBackoff == DateTime.MinValue
                    ? "Authorization failed."
                    : "Authorization failed. No retries will be made until: " + this.authRetryBackoff;

                return new GitEndPointResponseData(
                    HttpStatusCode.Unauthorized,
                    new HttpGitObjectsException(HttpStatusCode.Unauthorized, message),
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
                HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string contentType = string.Empty;
                    IEnumerable<string> values;
                    if (response.Content.Headers.TryGetValues("Content-Type", out values))
                    {
                        contentType = values.First();
                    }

                    this.credentialHasBeenRevoked = false;
                    Stream responseStream = response.Content.ReadAsStreamAsync().Result;
                    return new GitEndPointResponseData(response.StatusCode, contentType, responseStream);
                }
                else
                {
                    string errorMessage = response.Content.ReadAsStringAsync().Result;
                    int statusInt = (int)response.StatusCode;

                    if (string.IsNullOrWhiteSpace(errorMessage))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            lock (this.gitAuthorizationLock)
                            {
                                // Wipe the username and password so we can try recovering if applicable.
                                this.gitAuthorization = null;
                                if (!this.credentialHasBeenRevoked)
                                {
                                    GitProcess.RevokeCredential(this.enlistment);
                                    this.credentialHasBeenRevoked = true;
                                    return new GitEndPointResponseData(
                                        response.StatusCode,
                                        new HttpGitObjectsException(response.StatusCode, "Server returned error code 401 (Unauthorized). Your PAT may be expired."),
                                        shouldRetry: true);
                                }
                                else
                                {
                                    this.authRetryBackoff = DateTime.MaxValue;
                                    return new GitEndPointResponseData(
                                        response.StatusCode,
                                        new HttpGitObjectsException(response.StatusCode, "Server returned error code 401 (Unauthorized) after successfully renewing your PAT. You may not have access to this repo"),
                                        shouldRetry: false);
                                }
                            }
                        }
                        else
                        {
                            errorMessage = string.Format("Server returned error code {0} ({1})", statusInt, response.StatusCode);
                        }
                    }

                    return new GitEndPointResponseData(response.StatusCode, new HttpGitObjectsException(response.StatusCode, errorMessage), ShouldRetry(response.StatusCode));
                }
            }
            catch (TaskCanceledException)
            {
                string errorMessage = string.Format("Request to {0} timed out", requestUri);
                return new GitEndPointResponseData(HttpStatusCode.RequestTimeout, new HttpGitObjectsException(HttpStatusCode.RequestTimeout, errorMessage), shouldRetry: true);
            }
            catch (WebException ex)
            {
                return new GitEndPointResponseData(HttpStatusCode.InternalServerError, ex, shouldRetry: true);
            }
        }

        private string ObjectIdsJsonGenerator(Func<IEnumerable<string>> objectIdGenerator, int commitDepth)
        {
            IEnumerable<string> objectIds = objectIdGenerator();
            string objectIdsJson = CreateObjectIdJson(objectIds, commitDepth);
            int objectCount = objectIds.Count();
            EventMetadata metadata = new EventMetadata();
            if (objectCount < 10)
            {
                metadata.Add("ObjectIds", string.Join(", ", objectIds));
            }
            else
            {
                metadata.Add("ObjectIdCount", objectCount);
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "DownloadObjects", metadata, Keywords.Network);
            return objectIdsJson;
        }

        public class GitObjectSize
        {
            public readonly string Id;
            public readonly long Size;

            [JsonConstructor]
            public GitObjectSize(string id, long size)
            {
                this.Id = id;
                this.Size = size;
            }
        }

        public class GitObjectTaskResult
        {
            public GitObjectTaskResult(bool success)
            {
                this.Success = success;
            }

            public GitObjectTaskResult(HttpStatusCode statusCode)
                : this(statusCode == HttpStatusCode.OK)
            {
                this.HttpStatusCodeResult = statusCode;
            }

            public bool Success { get; }
            public HttpStatusCode HttpStatusCodeResult { get; }
        }

        public class GitEndPointResponseData
        {
            /// <summary>
            /// Constructor used when GitEndPointResponseData contains an error response
            /// </summary>
            public GitEndPointResponseData(HttpStatusCode statusCode, Exception error, bool shouldRetry)
            {
                this.StatusCode = statusCode;
                this.Error = error;
                this.ShouldRetry = shouldRetry;
            }

            /// <summary>
            /// Constructor used when GitEndPointResponseData contains a successful response
            /// </summary>
            public GitEndPointResponseData(HttpStatusCode statusCode, string contentType, Stream responseStream)
                : this(statusCode, null, false)
            {
                this.Stream = responseStream;
                this.ContentType = MapContentType(contentType);
            }

            /// <summary>
            /// Stream returned by a successful response.  If the response is an error, Stream will be null
            /// </summary>
            public Stream Stream { get; }

            public Exception Error { get; }

            public bool ShouldRetry { get; }

            public HttpStatusCode StatusCode { get; }

            public bool HasErrors
            {
                get { return this.StatusCode != HttpStatusCode.OK; }
            }

            public ContentType ContentType { get; }

            /// <summary>
            /// Convert from a string-based Content-Type to <see cref="HttpGitObjects.ContentType"/> 
            /// </summary>
            private static ContentType MapContentType(string contentType)
            {
                switch (contentType)
                {
                    case GVFSConstants.MediaTypes.LooseObjectMediaType:
                        return ContentType.LooseObject;
                    case GVFSConstants.MediaTypes.CustomLooseObjectsMediaType:
                        return ContentType.BatchedLooseObjects;
                    case GVFSConstants.MediaTypes.PackFileMediaType:
                        return ContentType.PackFile;
                    default:
                        return ContentType.None;
                }
            }
        }

        public class HttpGitObjectsException : Exception
        {
            public HttpGitObjectsException(HttpStatusCode statusCode, string ex) : base(ex)
            {
                this.StatusCode = statusCode;
            }

            public HttpStatusCode StatusCode { get; }
        }

        private class BootstrapResponse
        {
            public IList<Uri> PackUris { get; set; }
        }
    }
}