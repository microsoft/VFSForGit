using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
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
            Uri preferredCacheServerEndpoint = new Uri(this.CacheServer.SizesEndpointUrl);
            Uri globalCacheServerEndpoint = new Uri(this.CacheServer.GlobalSizesEndpointUrl);
            Uri originEndpoint = new Uri(this.enlistment.RepoUrl + GVFSConstants.Endpoints.GVFSSizes);
            bool hasEndpointOverride = preferredCacheServerEndpoint != globalCacheServerEndpoint;
            bool useGlobalCacheServer = !hasEndpointOverride;
            bool useOrigin = this.nextCacheServerAttemptTime >= DateTime.Now;

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

            RetryWrapper<List<GitObjectSize>> retrier = new RetryWrapper<List<GitObjectSize>>(
                this.RetryConfig.MaxAttempts + (hasEndpointOverride && !useOrigin ? 2 : 0),
                cancellationToken);
            retrier.OnFailure += RetryWrapper<List<GitObjectSize>>.StandardErrorHandler(this.Tracer, requestId, "QueryFileSizes");

            RetryWrapper<List<GitObjectSize>>.InvocationResult requestTask = retrier.Invoke(
                tryCount =>
                {
                    Uri gvfsEndpoint;
                    if (useOrigin)
                    {
                        gvfsEndpoint = originEndpoint;
                    }
                    else if (useGlobalCacheServer)
                    {
                        gvfsEndpoint = globalCacheServerEndpoint;
                    }
                    else
                    {
                        gvfsEndpoint = preferredCacheServerEndpoint;
                    }

                    try
                    {
                        using (GitEndPointResponseData response = this.SendProtocolRequest(requestId, gvfsEndpoint, HttpMethod.Post, objectIdsJson, cancellationToken))
                        {
                            if (response.HasErrors && !useGlobalCacheServer && !useOrigin)
                            {
                                this.TraceCacheServerFallback(
                                    requestId,
                                    preferredCacheServerEndpoint,
                                    globalCacheServerEndpoint,
                                    "EndpointSpecific",
                                    "Global");
                                useGlobalCacheServer = true;
                                return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                                    response.Error,
                                    shouldRetry: true,
                                    result: null,
                                    shouldRecordFailure: false);
                            }

                            if (response.StatusCode == HttpStatusCode.NotFound)
                            {
                                if (!useOrigin)
                                {
                                    this.TraceCacheServerFallback(
                                        requestId,
                                        globalCacheServerEndpoint,
                                        originEndpoint,
                                        "Global",
                                        "Origin");
                                }

                                this.nextCacheServerAttemptTime = DateTime.Now.AddDays(1);
                                useOrigin = true;
                                return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                                    response.Error,
                                    shouldRetry: true,
                                    result: null,
                                    shouldRecordFailure: false);
                            }

                            if (response.HasErrors)
                            {
                                return new RetryWrapper<List<GitObjectSize>>.CallbackResult(response.Error, response.ShouldRetry);
                            }

                            string objectSizesString = response.RetryableReadToEnd();
                            List<GitObjectSize> objectSizes = GVFSJsonOptions.Deserialize<List<GitObjectSize>>(objectSizesString);
                            return new RetryWrapper<List<GitObjectSize>>.CallbackResult(objectSizes);
                        }
                    }
                    catch (Exception e) when (
                        (e is HttpRequestException || e is IOException || e is RetryableException) &&
                        !useGlobalCacheServer &&
                        !useOrigin)
                    {
                        this.TraceCacheServerFallback(requestId, preferredCacheServerEndpoint, globalCacheServerEndpoint, "EndpointSpecific", "Global");
                        useGlobalCacheServer = true;
                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                            e,
                            shouldRetry: true,
                            result: null,
                            shouldRecordFailure: false);
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
                    using (GitEndPointResponseData response = this.SendProtocolRequest(
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
                new Uri(this.CacheServer.ObjectsGetEndpointUrl + "/" + objectId),
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
                () => new Uri(this.CacheServer.ObjectsPostEndpointUrl),
                requestBodyGenerator: () => this.ObjectIdsJsonGenerator(requestId, objectIdGenerator),
                cancellationToken: CancellationToken.None,
                acceptType: preferBatchedLooseObjects ? CustomLooseObjectsHeader : null,
                fallbackEndPointGenerator: () => new Uri(this.CacheServer.ObjectsEndpointUrl));
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
                new Uri(this.CacheServer.ObjectsPostEndpointUrl),
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
            Uri fallbackEndPoint,
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
                () => endPoint,
                requestBodyGenerator: () => requestBody,
                cancellationToken: cancellationToken,
                acceptType: acceptType,
                retryOnFailure: retryOnFailure,
                fallbackEndPointGenerator: () => fallbackEndPoint);
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
                fallbackEndPoint: null,
                cancellationToken: cancellationToken,
                requestBody: requestBody,
                acceptType: acceptType,
                retryOnFailure: retryOnFailure);
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
                requestBodyGenerator: requestBodyGenerator,
                cancellationToken: cancellationToken,
                acceptType: acceptType,
                retryOnFailure: retryOnFailure);
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
            bool retryOnFailure = true,
            Func<Uri> fallbackEndPointGenerator = null)
        {
            Uri endPoint = endPointGenerator();
            Uri fallbackEndPoint = fallbackEndPointGenerator?.Invoke();
            bool hasFallbackEndPoint = fallbackEndPoint != null && endPoint != fallbackEndPoint;
            bool useFallbackEndPoint = false;

            RetryWrapper<GitObjectTaskResult> retrier = new RetryWrapper<GitObjectTaskResult>(
                (retryOnFailure ? this.RetryConfig.MaxAttempts : 1) + (hasFallbackEndPoint ? 1 : 0),
                cancellationToken);
            if (onFailure != null)
            {
                retrier.OnFailure += onFailure;
            }

            return retrier.Invoke(
                tryCount =>
                {
                    Uri requestEndPoint = useFallbackEndPoint ? fallbackEndPointGenerator() : endPointGenerator();
                    GitEndPointResponseData response;

                    try
                    {
                        response = this.SendProtocolRequest(
                            requestId,
                            requestEndPoint,
                            method,
                            requestBodyGenerator(),
                            cancellationToken,
                            acceptType);
                    }
                    catch (HttpRequestException e)
                    {
                        return this.HandleProtocolException(
                            requestId,
                            e,
                            requestEndPoint,
                            fallbackEndPoint,
                            hasFallbackEndPoint,
                            ref useFallbackEndPoint,
                            retryOnFailure);
                    }
                    catch (IOException e)
                    {
                        return this.HandleProtocolException(
                            requestId,
                            e,
                            requestEndPoint,
                            fallbackEndPoint,
                            hasFallbackEndPoint,
                            ref useFallbackEndPoint,
                            retryOnFailure);
                    }
                    catch (RetryableException e)
                    {
                        return this.HandleProtocolException(
                            requestId,
                            e,
                            requestEndPoint,
                            fallbackEndPoint,
                            hasFallbackEndPoint,
                            ref useFallbackEndPoint,
                            retryOnFailure);
                    }

                    using (response)
                    {
                        if (response.HasErrors)
                        {
                            bool shouldFallBack = hasFallbackEndPoint && !useFallbackEndPoint;
                            if (shouldFallBack)
                            {
                                this.TraceCacheServerFallback(
                                    requestId,
                                    requestEndPoint,
                                    fallbackEndPoint,
                                    "EndpointSpecific",
                                    "Global");
                            }

                            useFallbackEndPoint |= shouldFallBack;
                            return new RetryWrapper<GitObjectTaskResult>.CallbackResult(
                                response.Error,
                                shouldFallBack || response.ShouldRetry,
                                new GitObjectTaskResult(response.StatusCode, requestEndPoint),
                                shouldRecordFailure: response.ShouldRetry && !shouldFallBack);
                        }

                        RetryWrapper<GitObjectTaskResult>.CallbackResult result;
                        try
                        {
                            result = onSuccess(tryCount, response);
                        }
                        catch (Exception e)
                        {
                            if (response.StreamReadFailed)
                            {
                                return this.HandleProtocolException(
                                    requestId,
                                    e,
                                    requestEndPoint,
                                    fallbackEndPoint,
                                    hasFallbackEndPoint,
                                    ref useFallbackEndPoint,
                                    retryOnFailure);
                            }

                            throw;
                        }

                        if (result.HasErrors)
                        {
                            bool shouldFallBack = response.StreamReadFailed && hasFallbackEndPoint && !useFallbackEndPoint;
                            if (shouldFallBack)
                            {
                                this.TraceCacheServerFallback(
                                    requestId,
                                    requestEndPoint,
                                    fallbackEndPoint,
                                    "EndpointSpecific",
                                    "Global");
                                useFallbackEndPoint = true;
                            }

                            GitObjectTaskResult requestResult = result.Result == null
                                ? new GitObjectTaskResult(success: false, requestEndPoint)
                                : result.Result.WithRequestUri(requestEndPoint);
                            return new RetryWrapper<GitObjectTaskResult>.CallbackResult(
                                result.Error,
                                shouldFallBack || result.ShouldRetry,
                                requestResult,
                                shouldRecordFailure: result.ShouldRecordFailure && !shouldFallBack);
                        }

                        return result;
                    }
                });
        }

        private RetryWrapper<GitObjectTaskResult>.CallbackResult HandleProtocolException(
            long requestId,
            Exception error,
            Uri requestEndPoint,
            Uri fallbackEndPoint,
            bool hasFallbackEndPoint,
            ref bool useFallbackEndPoint,
            bool retryOnFailure)
        {
            bool shouldFallBack = hasFallbackEndPoint && !useFallbackEndPoint;
            if (shouldFallBack)
            {
                this.TraceCacheServerFallback(
                    requestId,
                    requestEndPoint,
                    fallbackEndPoint,
                    "EndpointSpecific",
                    "Global");
                useFallbackEndPoint = true;
            }

            return new RetryWrapper<GitObjectTaskResult>.CallbackResult(
                error,
                shouldFallBack || retryOnFailure,
                new GitObjectTaskResult(success: false, requestEndPoint),
                shouldRecordFailure: retryOnFailure && !shouldFallBack);
        }

        private void TraceCacheServerFallback(
            long requestId,
            Uri source,
            Uri target,
            string sourceRoute,
            string targetRoute)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RequestId", requestId);
            metadata.Add("SourceRoute", sourceRoute);
            metadata.Add("SourceAuthority", GetAuthorityForTelemetry(source));
            metadata.Add("TargetRoute", targetRoute);
            metadata.Add("TargetAuthority", GetAuthorityForTelemetry(target));
            this.Tracer.RelatedEvent(EventLevel.Informational, "CacheServerFallback", metadata, Keywords.Network | Keywords.Telemetry);
        }

        protected virtual GitEndPointResponseData SendProtocolRequest(
            long requestId,
            Uri requestUri,
            HttpMethod httpMethod,
            string requestContent,
            CancellationToken cancellationToken,
            MediaTypeWithQualityHeaderValue acceptType = null)
        {
            return this.SendRequest(requestId, requestUri, httpMethod, requestContent, cancellationToken, acceptType);
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
            public string Id { get; set; }
            public long Size { get; set; }

            [JsonConstructor]
            public GitObjectSize(string id, long size)
            {
                this.Id = id;
                this.Size = size;
            }
        }

        public class GitObjectTaskResult
        {
            public GitObjectTaskResult(bool success, Uri requestUri = null)
            {
                this.Success = success;
                this.RequestUri = requestUri;
            }

            public GitObjectTaskResult(HttpStatusCode statusCode, Uri requestUri = null)
                : this(statusCode == HttpStatusCode.OK, requestUri)
            {
                this.HttpStatusCodeResult = statusCode;
            }

            public bool Success { get; }
            public HttpStatusCode HttpStatusCodeResult { get; private set; }
            public Uri RequestUri { get; }

            public GitObjectTaskResult WithRequestUri(Uri requestUri)
            {
                return new GitObjectTaskResult(this.Success, requestUri)
                {
                    HttpStatusCodeResult = this.HttpStatusCodeResult,
                };
            }
        }
    }
}