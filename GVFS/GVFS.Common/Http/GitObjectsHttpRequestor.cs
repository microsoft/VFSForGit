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
        private static readonly TimeSpan SizesAuthFailureCooldown = TimeSpan.FromSeconds(30);
        private readonly Lock sizesAuthLock = new Lock();
        private readonly Dictionary<Uri, SizesAuthFailure> sizesAuthFailures = new Dictionary<Uri, SizesAuthFailure>();
        private readonly Dictionary<Uri, long> sizesEndpointGenerations = new Dictionary<Uri, long>();
        private readonly TimeProvider timeProvider;

        public GitObjectsHttpRequestor(ITracer tracer, Enlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig)
            : this(tracer, enlistment, cacheServer, retryConfig, TimeProvider.System)
        {
        }

        internal GitObjectsHttpRequestor(
            ITracer tracer,
            Enlistment enlistment,
            CacheServerInfo cacheServer,
            RetryConfig retryConfig,
            TimeProvider timeProvider,
            HttpMessageHandler handlerOverride = null)
            : base(tracer, retryConfig, enlistment, handlerOverride)
        {
            this.enlistment = enlistment;
            this.CacheServer = cacheServer;
            this.timeProvider = timeProvider;
        }

        public CacheServerInfo CacheServer { get; private set; }

        public virtual List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            return this.QueryForFileSizes(objectIds, cancellationToken, out _);
        }

        public virtual List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken, out Exception error)
        {
            Dictionary<Uri, SizesAuthFailure> recoveryProbes = new Dictionary<Uri, SizesAuthFailure>();
            try
            {
                return this.QueryForFileSizes(objectIds, cancellationToken, recoveryProbes, out error);
            }
            finally
            {
                lock (this.sizesAuthLock)
                {
                    foreach (KeyValuePair<Uri, SizesAuthFailure> probe in recoveryProbes)
                    {
                        if (this.sizesAuthFailures.TryGetValue(probe.Key, out SizesAuthFailure failure) &&
                            ReferenceEquals(failure, probe.Value))
                        {
                            failure.ProbeInProgress = false;
                        }
                    }
                }
            }
        }

        private List<GitObjectSize> QueryForFileSizes(
            IEnumerable<string> objectIds,
            CancellationToken cancellationToken,
            Dictionary<Uri, SizesAuthFailure> recoveryProbes,
            out Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            int maxAttempts = this.RetryConfig.MaxAttempts + (hasEndpointOverride && !useOrigin ? 2 : 0);
            Uri initialEndpoint = useOrigin ? originEndpoint : (useGlobalCacheServer ? globalCacheServerEndpoint : preferredCacheServerEndpoint);
            while (this.TryGetSizesAuthFailure(initialEndpoint, recoveryProbes, out Exception cachedError, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!useGlobalCacheServer && !useOrigin)
                {
                    useGlobalCacheServer = true;
                    initialEndpoint = globalCacheServerEndpoint;
                    maxAttempts--;
                    continue;
                }

                error = cachedError;
                return new List<GitObjectSize>(0);
            }

            RetryWrapper<List<GitObjectSize>> retrier = new RetryWrapper<List<GitObjectSize>>(
                maxAttempts,
                cancellationToken,
                tracer: this.Tracer);
            bool suppressed = false;
            Action<RetryWrapper<List<GitObjectSize>>.ErrorEventArgs> logFailure =
                RetryWrapper<List<GitObjectSize>>.StandardErrorHandler(this.Tracer, requestId, "QueryFileSizes");
            retrier.OnFailure += failure =>
            {
                if (!suppressed)
                {
                    logFailure(failure);
                }
            };
            Uri lastEndpoint = null;
            (Exception Error, HttpStatusCode StatusCode, long Generation)? lastAuthFailure = null;
            Exception lastServerError = null;

            RetryWrapper<List<GitObjectSize>>.InvocationResult requestTask = retrier.Invoke(
                tryCount =>
                {
                    suppressed = false;
                    (Exception Error, HttpStatusCode StatusCode, long Generation)? previousAuthFailure = lastAuthFailure;
                    lastAuthFailure = null;
                    Exception previousServerError = lastServerError;
                    lastServerError = null;
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

                    if (this.TryGetSizesAuthFailure(gvfsEndpoint, recoveryProbes, out Exception cachedError, out long generation))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        suppressed = true;
                        if (!useGlobalCacheServer && !useOrigin)
                        {
                            useGlobalCacheServer = true;
                            return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                                cachedError, shouldRetry: true, result: null, shouldRecordFailure: false);
                        }

                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                            cachedError, shouldRetry: false, result: null, shouldRecordFailure: false);
                    }

                    lastEndpoint = gvfsEndpoint;
                    try
                    {
                        using (GitEndPointResponseData response = this.SendProtocolRequest(requestId, gvfsEndpoint, HttpMethod.Post, objectIdsJson, cancellationToken))
                        {
                            if (response.IsAuthenticationFailure)
                            {
                                lastAuthFailure = (response.Error, response.StatusCode, generation);
                                lastServerError = response.Error;
                            }
                            else if (response.IsCredentialFailure)
                            {
                                lastAuthFailure = previousAuthFailure;
                                lastServerError = previousServerError;
                            }

                            if (response.HasErrors && !useGlobalCacheServer && !useOrigin)
                            {
                                if (response.IsAuthenticationFailure)
                                {
                                    this.RecordSizesAuthFailure(requestId, gvfsEndpoint, lastAuthFailure.Value, recoveryProbes);
                                }

                                lastAuthFailure = null;
                                this.ReleaseSizesRecoveryProbe(gvfsEndpoint, recoveryProbes);
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
                                return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                                    response.Error,
                                    response.ShouldRetry,
                                    result: null,
                                    shouldRecordFailure: response.ShouldRecordFailure);
                            }

                            string objectSizesString = response.RetryableReadToEnd();
                            List<GitObjectSize> objectSizes = GVFSJsonOptions.Deserialize<List<GitObjectSize>>(objectSizesString);
                            this.ClearSizesAuthFailure(requestId, gvfsEndpoint, generation);
                            return new RetryWrapper<List<GitObjectSize>>.CallbackResult(objectSizes);
                        }
                    }
                    catch (Exception e) when (
                        (e is HttpRequestException || e is IOException || e is RetryableException) &&
                        !useGlobalCacheServer &&
                        !useOrigin)
                    {
                        this.ReleaseSizesRecoveryProbe(gvfsEndpoint, recoveryProbes);
                        this.TraceCacheServerFallback(requestId, preferredCacheServerEndpoint, globalCacheServerEndpoint, "EndpointSpecific", "Global");
                        useGlobalCacheServer = true;
                        return new RetryWrapper<List<GitObjectSize>>.CallbackResult(
                            e,
                            shouldRetry: true,
                            result: null,
                            shouldRecordFailure: false);
                    }
                });

            if (!requestTask.Succeeded && lastAuthFailure.HasValue)
            {
                this.RecordSizesAuthFailure(requestId, lastEndpoint, lastAuthFailure.Value, recoveryProbes);
            }

            error = lastServerError ?? requestTask.Error;
            return requestTask.Result ?? new List<GitObjectSize>(0);
        }

        private bool TryGetSizesAuthFailure(
            Uri endpoint,
            Dictionary<Uri, SizesAuthFailure> recoveryProbes,
            out Exception error,
            out long generation)
        {
            lock (this.sizesAuthLock)
            {
                error = null;
                this.sizesEndpointGenerations.TryGetValue(endpoint, out generation);
                if (!this.sizesAuthFailures.TryGetValue(endpoint, out SizesAuthFailure failure) ||
                    (recoveryProbes.TryGetValue(endpoint, out SizesAuthFailure probe) && ReferenceEquals(failure, probe)))
                {
                    return false;
                }

                if (!failure.ProbeInProgress &&
                    !RetryCircuitBreaker.IsOpen &&
                    this.timeProvider.GetElapsedTime(failure.LastAttemptTimestamp) >= SizesAuthFailureCooldown)
                {
                    failure.ProbeInProgress = true;
                    recoveryProbes[endpoint] = failure;
                    return false;
                }

                failure.SuppressedQueries++;
                error = failure.Error;
                return true;
            }
        }

        private void RecordSizesAuthFailure(
            long requestId,
            Uri endpoint,
            (Exception Error, HttpStatusCode StatusCode, long Generation) response,
            Dictionary<Uri, SizesAuthFailure> recoveryProbes)
        {
            EventMetadata metadata;
            lock (this.sizesAuthLock)
            {
                this.sizesEndpointGenerations.TryGetValue(endpoint, out long generation);
                if (generation != response.Generation)
                {
                    return;
                }

                bool firstFailure = !this.sizesAuthFailures.TryGetValue(endpoint, out SizesAuthFailure failure);
                if (!firstFailure &&
                    (!recoveryProbes.TryGetValue(endpoint, out SizesAuthFailure probe) || !ReferenceEquals(failure, probe)))
                {
                    return;
                }

                if (firstFailure)
                {
                    failure = new SizesAuthFailure { FirstFailureTimestamp = this.timeProvider.GetTimestamp() };
                    this.sizesAuthFailures.Add(endpoint, failure);
                }

                failure.Error = response.Error;
                failure.LastAttemptTimestamp = this.timeProvider.GetTimestamp();
                this.sizesEndpointGenerations[endpoint] = generation + 1;
                metadata = new EventMetadata
                {
                    { "RequestId", requestId },
                    { "Authority", GetAuthorityForTelemetry(endpoint) },
                    { "StatusCode", (int)response.StatusCode },
                    { "State", firstFailure ? "Entered" : "Continued" },
                    { "CooldownSeconds", SizesAuthFailureCooldown.TotalSeconds },
                    { "SuppressedQueries", failure.SuppressedQueries },
                    { "DurationSeconds", this.timeProvider.GetElapsedTime(failure.FirstFailureTimestamp).TotalSeconds },
                };
            }

            this.Tracer.RelatedEvent(EventLevel.Warning, "SizesAuthFailureCooldown", metadata, Keywords.Network | Keywords.Telemetry);
        }

        private void ReleaseSizesRecoveryProbe(Uri endpoint, Dictionary<Uri, SizesAuthFailure> recoveryProbes)
        {
            lock (this.sizesAuthLock)
            {
                if (recoveryProbes.Remove(endpoint, out SizesAuthFailure probe) &&
                    this.sizesAuthFailures.TryGetValue(endpoint, out SizesAuthFailure failure) &&
                    ReferenceEquals(failure, probe))
                {
                    failure.ProbeInProgress = false;
                }
            }
        }

        private void ClearSizesAuthFailure(long requestId, Uri endpoint, long generation)
        {
            EventMetadata metadata;
            lock (this.sizesAuthLock)
            {
                this.sizesEndpointGenerations.TryGetValue(endpoint, out long currentGeneration);
                if (currentGeneration != generation)
                {
                    return;
                }

                this.sizesEndpointGenerations[endpoint] = generation + 1;
                if (!this.sizesAuthFailures.Remove(endpoint, out SizesAuthFailure failure))
                {
                    return;
                }

                metadata = new EventMetadata
                {
                    { "RequestId", requestId },
                    { "Authority", GetAuthorityForTelemetry(endpoint) },
                    { "SuppressedQueries", failure.SuppressedQueries },
                    { "DurationSeconds", this.timeProvider.GetElapsedTime(failure.FirstFailureTimestamp).TotalSeconds },
                };
            }

            this.Tracer.RelatedEvent(EventLevel.Informational, "SizesAuthFailureRecovered", metadata, Keywords.Network | Keywords.Telemetry);
        }

        private sealed class SizesAuthFailure
        {
            public Exception Error { get; set; }
            public long FirstFailureTimestamp { get; set; }
            public long LastAttemptTimestamp { get; set; }
            public long SuppressedQueries { get; set; }
            public bool ProbeInProgress { get; set; }
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

            RetryWrapper<GitRefs> retrier = new RetryWrapper<GitRefs>(this.RetryConfig.MaxAttempts, CancellationToken.None, tracer: this.Tracer);
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
                            return new RetryWrapper<GitRefs>.CallbackResult(
                                response.Error,
                                response.ShouldRetry,
                                result: null,
                                shouldRecordFailure: response.ShouldRecordFailure);
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
                cancellationToken,
                tracer: this.Tracer);
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
                                shouldRecordFailure: response.ShouldRecordFailure && !shouldFallBack);
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