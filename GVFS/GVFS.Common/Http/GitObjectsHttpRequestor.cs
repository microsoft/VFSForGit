using GVFS.Common.Git;
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

namespace GVFS.Common.Http
{
    public class GitObjectsHttpRequestor : HttpRequestor
    {
        private static readonly MediaTypeWithQualityHeaderValue CustomLooseObjectsHeader
            = new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.CustomLooseObjectsMediaType);
                
        private Enlistment enlistment;

        public GitObjectsHttpRequestor(ITracer tracer, Enlistment enlistment, int maxConnections)
            : base(tracer, enlistment.Authentication, maxConnections)
        {
            this.enlistment = enlistment;
            this.MaxRetries = HttpRequestor.DefaultMaxRetries;
        }

        public int MaxRetries { get; set; }
        
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

            this.Tracer.RelatedEvent(EventLevel.Informational, "QueryFileSizes", metadata, Keywords.Network);

            RetryWrapper<List<GitObjectSize>> retrier = new RetryWrapper<List<GitObjectSize>>(this.MaxRetries);
            retrier.OnFailure += RetryWrapper<List<GitObjectSize>>.StandardErrorHandler(this.Tracer, "QueryFileSizes");

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
            retrier.OnFailure += RetryWrapper<GitRefs>.StandardErrorHandler(this.Tracer, "QueryInfoRefs");

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

        public virtual RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadLooseObject(
            string objectId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ObjectId", objectId);

            this.Tracer.RelatedEvent(EventLevel.Informational, "DownloadLooseObject", metadata, Keywords.Network);

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

            this.Tracer.RelatedEvent(EventLevel.Informational, "DownloadObjects", metadata, Keywords.Network);

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