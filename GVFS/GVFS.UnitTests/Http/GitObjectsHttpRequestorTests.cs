using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace GVFS.UnitTests.Http
{
    [TestFixture]
    public class GitObjectsHttpRequestorTests
    {
        private const string GlobalCacheServerUrl = "https://global-cache/server";
        private const string EndpointCacheServerUrl = "https://endpoint-cache/server";

        [SetUp]
        public void SetUp()
        {
            RetryCircuitBreaker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RetryCircuitBreaker.Reset();
        }

        [TestCase]
        public void LooseObjectFallsBackToGlobalCacheServerWhenEndpointRequestFails()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.NotFound);
            requestor.EnqueueResponse(HttpStatusCode.OK);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadLooseObject(
                    "0123456789abcdef",
                    retryOnFailure: false,
                    CancellationToken.None,
                    requestSource: "test",
                    onSuccess: SuccessfulRequest);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects/0123456789abcdef");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects/0123456789abcdef");
            requestor.TestTracer.RelatedEventNames.ShouldContain(name => name == "CacheServerFallback");
            requestor.TestTracer.RelatedEventKeywords.ShouldContain(
                keywords => (keywords & Keywords.Telemetry) == Keywords.Telemetry);
        }

        [TestCase]
        public void BatchedObjectRequestFallsBackToGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable, shouldRetry: true);
            requestor.EnqueueResponse(HttpStatusCode.NotFound);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(false);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void PrefetchRequestFallsBackToGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.BadRequest);
            requestor.EnqueueResponse(HttpStatusCode.OK);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TrySendProtocolRequest(
                    requestId: 1,
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    method: HttpMethod.Get,
                    endPointGenerator: () => new Uri(EndpointCacheServerUrl + "/gvfs/prefetch?lastPackTimestamp=0"),
                    fallbackEndPointGenerator: () => new Uri(GlobalCacheServerUrl + "/gvfs/prefetch?lastPackTimestamp=0"),
                    requestBodyGenerator: () => null,
                    cancellationToken: CancellationToken.None);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/prefetch?lastPackTimestamp=0");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/prefetch?lastPackTimestamp=0");
        }

        [TestCase]
        public void TransportExceptionFallsBackToGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueException(new HttpRequestException("Test failure"));
            requestor.EnqueueResponse(HttpStatusCode.NotFound);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(false);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void ResponseBodyReadFailureFallsBackToGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(new ThrowingReadStream());
            requestor.EnqueueResponse(HttpStatusCode.NotFound);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: (tryCount, response) =>
                    {
                        response.Stream.ReadByte();
                        return SuccessfulRequest(tryCount, response);
                    },
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(false);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void ResponseBodyReadFailureReportedByHandlerFallsBackToGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(new ThrowingReadStream());
            requestor.EnqueueResponse(HttpStatusCode.OK);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: (tryCount, response) =>
                    {
                        try
                        {
                            response.Stream.ReadByte();
                            return SuccessfulRequest(tryCount, response);
                        }
                        catch (IOException e)
                        {
                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                                e,
                                shouldRetry: true);
                        }
                    },
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void ResponseBodyCancellationIsNotRetriedOrReportedAsFallback()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1);
            requestor.EnqueueResponse(new CancelingReadStream());

            Assert.Throws<OperationCanceledException>(
                () => requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: (tryCount, response) =>
                    {
                        response.RetryableReadToEnd();
                        return SuccessfulRequest(tryCount, response);
                    },
                    onFailure: null,
                    preferBatchedLooseObjects: false));

            requestor.RequestUris.Count.ShouldEqual(1);
            requestor.TestTracer.RelatedEventNames.ShouldNotContain(name => name == "CacheServerFallback");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void FallbackTelemetryExcludesCredentialsAndRequestPath()
        {
            const string CredentialedGlobalUrl = "https://global-user:global-secret@global-cache:8443/server";
            const string CredentialedEndpointUrl = "https://endpoint-user:endpoint-secret@endpoint-cache:9443/server";
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(
                maxRetries: 0,
                globalCacheServerUrl: CredentialedGlobalUrl,
                endpointCacheServerUrl: CredentialedEndpointUrl);
            requestor.EnqueueResponse(HttpStatusCode.NotFound);
            requestor.EnqueueResponse(HttpStatusCode.OK);

            requestor.TryDownloadLooseObject(
                "0123456789abcdef",
                retryOnFailure: false,
                CancellationToken.None,
                requestSource: "test",
                onSuccess: SuccessfulRequest);

            int fallbackEventIndex = requestor.TestTracer.RelatedEventNames.IndexOf("CacheServerFallback");
            EventMetadata metadata = requestor.TestTracer.RelatedEventMetadata[fallbackEventIndex];
            metadata["SourceAuthority"].ShouldEqual("endpoint-cache:9443");
            metadata["TargetAuthority"].ShouldEqual("global-cache:8443");
        }

        [TestCase]
        public void NoEndpointOverrideUsesNormalGlobalCacheRetries()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable, shouldRetry: true);
            requestor.EnqueueResponse(HttpStatusCode.OK);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            requestor.TestTracer.RelatedEventNames.ShouldNotContain(name => name == "CacheServerFallback");
        }

        [TestCase]
        public void TerminalFallbackFailureReportsGlobalCacheServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
            requestor.EnqueueResponse(HttpStatusCode.NotFound);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(false);
            result.Attempts.ShouldEqual(2);
            result.Result.HttpStatusCodeResult.ShouldEqual(HttpStatusCode.NotFound);
            result.Result.RequestUri.AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/objects");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void SuccessHandlerFailureRetriesTheEndpointSpecificServer()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1);
            requestor.EnqueueResponse(HttpStatusCode.OK);
            requestor.EnqueueResponse(HttpStatusCode.OK);
            int successHandlerCalls = 0;

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TryDownloadObjects(
                    new[] { "0123456789abcdef" },
                    onSuccess: (tryCount, response) =>
                    {
                        if (++successHandlerCalls == 1)
                        {
                            throw new RetryableException("Local write failed");
                        }

                        return SuccessfulRequest(tryCount, response);
                    },
                    onFailure: null,
                    preferBatchedLooseObjects: false);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/objects");
            requestor.TestTracer.RelatedEventNames.ShouldNotContain(name => name == "CacheServerFallback");
        }

        [TestCase]
        public void SizesRequestFallsBackThroughGlobalCacheServerToOrigin()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
            requestor.EnqueueResponse(HttpStatusCode.NotFound);
            requestor.EnqueueResponse(HttpStatusCode.OK, "[]");

            requestor.QueryForFileSizes(new[] { "0123456789abcdef" }, CancellationToken.None);

            requestor.RequestUris.Count.ShouldEqual(3);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/sizes");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/sizes");
            requestor.RequestUris[2].AbsoluteUri.ShouldEqual("mock://repourl/gvfs/sizes");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void SizesHttpFallbackDoesNotChargeCircuitBreaker()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable, shouldRetry: true);
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable);

            requestor.QueryForFileSizes(new[] { "0123456789abcdef" }, CancellationToken.None);

            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/sizes");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/sizes");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        public void SizesTransportFallbackDoesNotChargeCircuitBreaker()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0);
            requestor.EnqueueException(new HttpRequestException("Test failure"));
            requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable);

            requestor.QueryForFileSizes(new[] { "0123456789abcdef" }, CancellationToken.None);

            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.RequestUris[0].AbsoluteUri.ShouldEqual(EndpointCacheServerUrl + "/gvfs/sizes");
            requestor.RequestUris[1].AbsoluteUri.ShouldEqual(GlobalCacheServerUrl + "/gvfs/sizes");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        private static RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult SuccessfulRequest(
            int tryCount,
            GitEndPointResponseData response)
        {
            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                new GitObjectsHttpRequestor.GitObjectTaskResult(true));
        }

        private TestGitObjectsHttpRequestor CreateRequestor(
            int maxRetries,
            bool endpointOverrides = true,
            string globalCacheServerUrl = GlobalCacheServerUrl,
            string endpointCacheServerUrl = EndpointCacheServerUrl)
        {
            CacheServerInfo cacheServer = new CacheServerInfo(globalCacheServerUrl, "global");
            if (endpointOverrides)
            {
                cacheServer = cacheServer.WithEndpointOverrides(
                    endpointCacheServerUrl,
                    endpointCacheServerUrl,
                    endpointCacheServerUrl,
                    endpointCacheServerUrl);
            }

            return new TestGitObjectsHttpRequestor(
                new MockGVFSEnlistment(),
                cacheServer,
                new RetryConfig(maxRetries));
        }

        private class TestGitObjectsHttpRequestor : GitObjectsHttpRequestor
        {
            private readonly Queue<object> responses = new Queue<object>();

            public TestGitObjectsHttpRequestor(
                Enlistment enlistment,
                CacheServerInfo cacheServer,
                RetryConfig retryConfig)
                : this(new MockTracer(), enlistment, cacheServer, retryConfig)
            {
            }

            private TestGitObjectsHttpRequestor(
                MockTracer tracer,
                Enlistment enlistment,
                CacheServerInfo cacheServer,
                RetryConfig retryConfig)
                : base(tracer, enlistment, cacheServer, retryConfig)
            {
                this.TestTracer = tracer;
                this.RequestUris = new List<Uri>();
            }

            public MockTracer TestTracer { get; }
            public List<Uri> RequestUris { get; }

            public void EnqueueResponse(HttpStatusCode statusCode, string body = "", bool shouldRetry = false)
            {
                this.responses.Enqueue(Tuple.Create(statusCode, body, shouldRetry));
            }

            public void EnqueueException(Exception exception)
            {
                this.responses.Enqueue(exception);
            }

            public void EnqueueResponse(Stream stream)
            {
                this.responses.Enqueue(stream);
            }

            protected override GitEndPointResponseData SendProtocolRequest(
                long requestId,
                Uri requestUri,
                HttpMethod httpMethod,
                string requestContent,
                CancellationToken cancellationToken,
                MediaTypeWithQualityHeaderValue acceptType = null)
            {
                this.RequestUris.Add(requestUri);
                object nextResponse = this.responses.Dequeue();
                if (nextResponse is Exception exception)
                {
                    throw exception;
                }

                if (nextResponse is Stream stream)
                {
                    return new GitEndPointResponseData(
                        HttpStatusCode.OK,
                        "application/json",
                        stream,
                        message: null,
                        onResponseDisposed: null);
                }

                Tuple<HttpStatusCode, string, bool> response = (Tuple<HttpStatusCode, string, bool>)nextResponse;

                if (response.Item1 == HttpStatusCode.OK)
                {
                    return new GitEndPointResponseData(
                        response.Item1,
                        "application/json",
                        new MemoryStream(Encoding.UTF8.GetBytes(response.Item2)),
                        message: null,
                        onResponseDisposed: null);
                }

                return new GitEndPointResponseData(
                    response.Item1,
                    new GitObjectsHttpException(response.Item1, "Test failure"),
                    shouldRetry: response.Item3,
                    message: null,
                    onResponseDisposed: null);
            }
        }

        private class ThrowingReadStream : MemoryStream
        {
            public override int ReadByte()
            {
                throw new IOException("Response body read failed");
            }
        }

        private class CancelingReadStream : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new OperationCanceledException("Response body read canceled");
            }
        }
    }
}
