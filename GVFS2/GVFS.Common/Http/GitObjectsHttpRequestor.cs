using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace GVFS.Common.Http
{
    public class GitObjectsHttpRequestor : HttpRequestor
    {
        private static readonly MediaTypeWithQualityHeaderValue CustomLooseObjectsHeader
            = new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.CustomLooseObjectsMediaType);

        private Enlistment enlistment;

        private DateTime nextCacheServerAttemptTime = DateTime.Now;

        public GitObjectsHttpRequestor(ITracer tracer, Enlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig)
            : base(tracer, retryConfig, enlistment)
        {
            this.enlistment = enlistment;
            this.CacheServer = cacheServer;
        }

        public CacheServerInfo CacheServer { get; private set; }

        public virtual List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            long requestId = HttpRequestor.GetNewRequestId();

            string objectIdsJson = ToJsonList(objectIds);
            Uri cacheServerEndpoint = new Uri(this.CacheServer.SizesEndpointUrl);
            Uri originEndpoint = new Uri(this.enlistment.RepoUrl + GVFSConstants.Endpoints.GVFSSizes);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("RequestId", requestId);
            int objectIdCount = objectIds.Count();
            if (objectIdCount > 10)
            {
                metadata.Add("ObjectIdCount", objectIdCount);
            }
            else
            {
                metadata.Add("ObjectIdJson", objectIdsJson);
            }

            this.Tracer.RelatedEvent(EventLevel.Informational, "QueryFileSizes", metadata, Keywords.Network);

            RetryWrapper<List<GitObjectSize>> retrier = new RetryWrapper<List<GitObjectSize>>(this.RetryConfig.MaxAttempts, cancellationToken);
            retrier.OnFailure += RetryWrapper<List<GitObjectSize>>.StandardErrorHandler(this.Tracer, requestId, "QueryFileSizes");

            RetryWrapper<List<GitObjectSize>>.InvocationResult requestTask = retrier.Invoke(
                tryCount =>
                {
                    Uri gvfsEndpoint;
                    if (this.nextCacheServerAttemptTime < DateTime.Now)
                    {
                        gvfsEndpoint = cacheServerEndpoint;
                    }
                    else
                    {
                        gvfsEndpoint = originEndpoint;
                    }

                    using (GitEndPointResponseData response = this.SendRequest(requestId, gvfsEndpoint, HttpMethod.Post, objectIdsJson, cancellationToken))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            this.nextCacheServerAttemptTime = DateTime.Now.AddDays(1);
                            return new RetryWrapper<List<GitObjectSize>>.CallbackResult(response.Error, true);
                        }

                        if (response.HasErrors)
                        {
                            return new RetryWrapper<List<GitObjectSize>>.CallbackResult(response.Error, response.ShouldRetry);
                        }

                        string objectSizesString = response.RetryableReadToEnd();
                        List<GitObjectSize> objectSizes = JsonConvert.DeserializeObject<List<GitObjectSize>>(objectSizesString);
                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(objectSizes);
                    }
                });

            return requestTask.Result ?? new List<GitObjectSize>(0);
        }

        public virtual GitRefs QueryInfoRefs(string branch)
        {
            long requestId = HttpRequestor.GetNewRequestId();

            Uri infoRefsEndpoint;
            try
            {
                infoRefsEndpoint = new Uri(this.enlistment.RepoUrl + GVFSConstants.Endpoints.InfoRefs);
            }
            catch (UriFormatException)
            {
                return null;
            }

            RetryWrapper<GitRefs> retrier = new RetryWrapper<GitRefs>(this.RetryConfig.MaxAttempts, CancellationToken.None);
            retrier.OnFailure += RetryWrapper<GitRefs>.StandardErrorHandler(this.Tracer, requestId, "QueryInfoRefs");

            RetryWrapper<GitRefs>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    using (GitEndPointResponseData response = this.SendRequest(
                        requestId,
                        infoRefsEndpoint,
                        HttpMethod.Get,
                        requestContent: null,
                        cancellationToken: CancellationToken.None))
                    {
                        if (response.HasErrors)
                        {
                            return new RetryWrapper<GitRefs>.CallbackResult(response.Error, response.ShouldRetry);
                        }

                        List<string> infoRefsResponse = response.RetryableReadAllLines();
                        return new RetryWrapper<GitRefs>.CallbackResult(new GitRefs(infoRefsResponse, branch));
                    }
                });

            return output.Result;
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadLooseObject(
            string objectId,
            bool retryOnFailure,
            CancellationToken cancellationToken,
            string requestSource,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess)
        {
            long requestId = HttpRequestor.GetNewRequestId();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("objectId", objectId);
            metadata.Add("retryOnFailure", retryOnFailure);
            metadata.Add("requestId", requestId);
            metadata.Add("requestSource", requestSource);
            this.Tracer.RelatedEvent(EventLevel.Informational, "DownloadLooseObject", metadata, Keywords.Network);

            return this.TrySendProtocolRequest(
                requestId,
                onSuccess,
                eArgs => this.HandleDownloadAndSaveObjectError(retryOnFailure, requestId, eArgs),
                HttpMethod.Get,
                new Uri(this.CacheServer.ObjectsEndpointUrl + "/" + objectId),
                cancellationToken,
                requestBody: null,
                acceptType: null,
                retryOnFailure: retryOnFailure);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            Func<IEnumerable<string>> objectIdGenerator,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            // We pass the query generator in as a function because we don't want the consumer to know about JSON or network retry logic,
            // but we still want the consumer to be able to change the query on each retry if we fail during their onSuccess handler.
            long requestId = HttpRequestor.GetNewRequestId();
            return this.TrySendProtocolRequest(
                requestId,
                onSuccess,
                onFailure,
                HttpMethod.Post,
                new Uri(this.CacheServer.ObjectsEndpointUrl),
                CancellationToken.None,
                () => this.ObjectIdsJsonGenerator(requestId, objectIdGenerator),
                preferBatchedLooseObjects ? CustomLooseObjectsHeader : null);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            IEnumerable<string> objectIds,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            long requestId = HttpRequestor.GetNewRequestId();

            string objectIdsJson = CreateObjectIdJson(objectIds);
            int objectCount = objectIds.Count();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RequestId", requestId);
            if (objectCount < 10)
            {
                metadata.Add("ObjectIds", string.Join(", ", objectIds));
            }
            else
            {
                metadata.Add("ObjectIdCount", objectCount);
            }

            this.Tracer.RelatedEvent(EventLevel.Informational, "DownloadObjects", metadata, Keywords.Network);

            return this.TrySendProtocolRequest(
                requestId,
                onSuccess,
                onFailure,
                HttpMethod.Post,
                new Uri(this.CacheServer.ObjectsEndpointUrl),
                CancellationToken.None,
                objectIdsJson,
                preferBatchedLooseObjects ? CustomLooseObjectsHeader : null);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
            long requestId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            HttpMethod method,
            Uri endPoint,
            CancellationToken cancellationToken,
            string requestBody = null,
            MediaTypeWithQualityHeaderValue acceptType = null,
            bool retryOnFailure = true)
        {
            return this.TrySendProtocolRequest(
                requestId,
                onSuccess,
                onFailure,
                method,
                endPoint,
                cancellationToken,
                () => requestBody,
                acceptType,
                retryOnFailure);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
            long requestId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            HttpMethod method,
            Uri endPoint,
            CancellationToken cancellationToken,
            Func<string> requestBodyGenerator,
            MediaTypeWithQualityHeaderValue acceptType = null,
            bool retryOnFailure = true)
        {
            return this.TrySendProtocolRequest(
                requestId,
                onSuccess,
                onFailure,
                method,
                () => endPoint,
                requestBodyGenerator,
                cancellationToken,
                acceptType,
                retryOnFailure);
        }

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
            long requestId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            HttpMethod method,
            Func<Uri> endPointGenerator,
            Func<string> requestBodyGenerator,
            CancellationToken cancellationToken,
            MediaTypeWithQualityHeaderValue acceptType = null,
            bool retryOnFailure = true)
        {
            RetryWrapper<GitObjectTaskResult> retrier = new RetryWrapper<GitObjectTaskResult>(
                retryOnFailure ? this.RetryConfig.MaxAttempts : 1,
                cancellationToken);
            if (onFailure != null)
            {
                retrier.OnFailure += onFailure;
            }

            return retrier.Invoke(
                tryCount =>
                {
                    using (GitEndPointResponseData response = this.SendRequest(
                        requestId,
                        endPointGenerator(),
                        method,
                        requestBodyGenerator(),
                        cancellationToken,
                        acceptType))
                    {
                        if (response.HasErrors)
                        {
                            return new RetryWrapper<GitObjectTaskResult>.CallbackResult(response.Error, response.ShouldRetry, new GitObjectTaskResult(response.StatusCode));
                        }

                        return onSuccess(tryCount, response);
                    }
                });
        }

        private static string ToJsonList(IEnumerable<string> strings)
        {
            return "[\"" + string.Join("\",\"", strings) + "\"]";
        }

        private static string CreateObjectIdJson(IEnumerable<string> strings)
        {
            return "{\"commitDepth\": 1, \"objectIds\":" + ToJsonList(strings) + "}";
        }

        private void HandleDownloadAndSaveObjectError(bool retryOnFailure, long requestId, RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.ErrorEventArgs errorArgs)
        {
            // Silence logging 404's for object downloads. They are far more likely to be git checking for the
            // previous existence of a new object than a truly missing object.
            GitObjectsHttpException ex = errorArgs.Error as GitObjectsHttpException;
            if (ex != null && ex.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            // If the caller has requested that we not retry on failure, caller must handle logging errors
            bool forceLogAsWarning = !retryOnFailure;
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.StandardErrorHandler(this.Tracer, requestId, nameof(this.TryDownloadLooseObject), forceLogAsWarning)(errorArgs);
        }

        private string ObjectIdsJsonGenerator(long requestId, Func<IEnumerable<string>> objectIdGenerator)
        {
            IEnumerable<string> objectIds = objectIdGenerator();
            string objectIdsJson = CreateObjectIdJson(objectIds);
            int objectCount = objectIds.Count();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RequestId", requestId);
            if (objectCount < 10)
            {
                metadata.Add("ObjectIds", string.Join(", ", objectIds));
            }
            else
            {
                metadata.Add("ObjectIdCount", objectCount);
            }

            this.Tracer.RelatedEvent(EventLevel.Informational, "DownloadObjects", metadata, Keywords.Network);
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
    }
}