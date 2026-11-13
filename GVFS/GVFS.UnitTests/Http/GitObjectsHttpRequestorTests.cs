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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GVFS.UnitTests.Tracing;

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
        public void FileSizeAndObjectRequestorsObserveOpenBreakerState()
        {
            RetryCircuitBreaker.Configure(1, 30000);
            RetryCircuitBreaker.RecordFailure();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.QueryForFileSizes(new[] { "0123456789abcdef" }, CancellationToken.None);
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Contain("RetryCircuitBreakerState"));
                Assert.That(requestor.RequestUris, Is.Empty);
            }

            RetryCircuitBreaker.Reset();
            RetryCircuitBreaker.Configure(1, 30000);
            RetryCircuitBreaker.RecordFailure();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.TryDownloadLooseObject("0123456789abcdef", false, CancellationToken.None, "test", SuccessfulRequest);
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Contain("RetryCircuitBreakerState"));
                Assert.That(requestor.RequestUris, Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfigRequestorPreservesSilentErrorPolicy(bool logErrors)
        {
            MockTracer tracer = new MockTracer();
            RetryCircuitBreaker.Configure(1, 30000);
            RetryCircuitBreaker.RecordFailure();
            using (ConfigHttpRequestor requestor = new ConfigHttpRequestor(tracer, new MockGVFSEnlistment(), new RetryConfig(maxRetries: 0)))
            {
                Assert.That(requestor.TryQueryGVFSConfig(logErrors, out _, out _, out _), Is.False);
                Assert.That(tracer.RelatedEventNames.Contains("RetryCircuitBreakerState"), Is.EqualTo(logErrors));
                Assert.That(tracer.RelatedErrorEvents.Count > 0, Is.EqualTo(logErrors));
            }
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
        public void RetryableAuthenticationChallengeDoesNotIncrementCircuitBreaker()
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1);
            requestor.EnqueueResponse(
                HttpStatusCode.Unauthorized,
                shouldRetry: true,
                shouldRecordFailure: false);
            requestor.EnqueueResponse(HttpStatusCode.OK);

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                requestor.TrySendProtocolRequest(
                    requestId: 1,
                    onSuccess: SuccessfulRequest,
                    onFailure: null,
                    method: HttpMethod.Get,
                    endPointGenerator: () => new Uri(EndpointCacheServerUrl + "/gvfs/objects"),
                    requestBodyGenerator: () => null,
                    cancellationToken: CancellationToken.None);

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
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

        [TestCase(false)]
        [TestCase(true)]
        public void ResponseBodyReadTimeoutUsesExistingRetryOrFallback(bool endpointOverrides)
        {
            TestGitObjectsHttpRequestor requestor = this.CreateRequestor(
                maxRetries: endpointOverrides ? 0 : 1,
                endpointOverrides: endpointOverrides);
            requestor.RetryConfig.ReadTimeout = TimeSpan.FromMilliseconds(100);
            requestor.EnqueueResponse(new StallingReadStream());
            requestor.EnqueueResponse(HttpStatusCode.OK);

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

            result.Succeeded.ShouldEqual(true);
            requestor.RequestUris.Count.ShouldEqual(2);
            requestor.TestTracer.RelatedEventNames.ShouldContain(name => name == "NetworkBodyReadTimeout");
            requestor.TestTracer.RelatedEventNames.Contains("CacheServerFallback").ShouldEqual(endpointOverrides);
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

        [TestCase(HttpStatusCode.BadRequest, false)]
        [TestCase(HttpStatusCode.Unauthorized, true)]
        public void RepeatedSizesAuthFailuresDoNotSendRequestsDuringCooldown(HttpStatusCode status, bool retry)
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false))
            {
                requestor.EnqueueResponse(status, "Authentication failed", retry, isAuthenticationFailure: true);
                if (retry)
                {
                    requestor.EnqueueResponse(status, "Authentication failed", retry, isAuthenticationFailure: true);
                }

                Assert.That(requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error), Is.Empty);
                int requests = requestor.RequestUris.Count;
                int failures = RetryCircuitBreaker.ConsecutiveFailures;
                for (int i = 0; i < 20; i++)
                {
                    Assert.That(requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception cachedError), Is.Empty);
                    Assert.That(cachedError, Is.SameAs(error));
                }

                Assert.That(requests, Is.EqualTo(retry ? 2 : 1));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(requests));
                Assert.That(failures, Is.EqualTo(retry ? 2 : 0));
                Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.EqualTo(failures));
                Assert.That(requestor.TestTracer.RelatedEventNames.Count(name => name == "SizesAuthFailureCooldown"), Is.EqualTo(1));
                Assert.That(requestor.TestTracer.RelatedErrorEvents.Count, Is.EqualTo(1));
            }
        }

        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.Unauthorized)]
        public void AnonymousSizesChallengeRetriesWithoutCooldown(HttpStatusCode status)
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false))
            {
                requestor.EnqueueResponse(status, shouldRetry: true, shouldRecordFailure: false);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");

                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                Assert.That(error, Is.Null);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
                Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.Zero);
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Not.Contain("SizesAuthFailureCooldown"));
            }
        }

        [TestCase]
        public void Sizes401CanRecoverWithinItsNormalRetryBudget()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false))
            {
                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, shouldRetry: true, isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);

                Assert.That(error, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Not.Contain("SizesAuthFailureCooldown"));
            }
        }

        [TestCase]
        public void CredentialBackoffAfterAServerRejectionStillEntersSizesCooldown()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false))
            {
                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, "Server rejected authentication", shouldRetry: true, isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, "Credential backoff is active", shouldRetry: true, isCredentialFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception cachedError);

                Assert.That(error.Message, Is.EqualTo("Server rejected authentication"));
                Assert.That(cachedError, Is.SameAs(error));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
            }
        }

        [TestCase(HttpStatusCode.BadRequest, false)]
        [TestCase(HttpStatusCode.Unauthorized, true)]
        [TestCase(HttpStatusCode.ServiceUnavailable, true)]
        public void OtherSizesFailuresDoNotEnterAuthCooldown(HttpStatusCode status, bool retry)
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false))
            {
                requestor.EnqueueResponse(status, "Not a server authentication rejection", retry, isCredentialFailure: status == HttpStatusCode.Unauthorized);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);

                Assert.That(error, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Not.Contain("SizesAuthFailureCooldown"));
            }
        }

        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.Unauthorized)]
        public void LatchedPreferredSizesEndpointStillAllowsGlobalRequests(HttpStatusCode status)
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.EnqueueResponse(status, isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception firstError);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception secondError);

                Assert.That(firstError, Is.Null);
                Assert.That(secondError, Is.Null);
                Assert.That(requestor.RequestUris.Select(uri => uri.AbsoluteUri), Is.EqualTo(new[]
                {
                    EndpointCacheServerUrl + "/gvfs/sizes",
                    GlobalCacheServerUrl + "/gvfs/sizes",
                    GlobalCacheServerUrl + "/gvfs/sizes",
                }));
            }
        }

        [TestCase]
        public void AllLatchedSizesEndpointsReturnTheCauseWithoutOriginFallback()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Preferred rejected", isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Global rejected", isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception cachedError);

                Assert.That(cachedError, Is.SameAs(error));
                Assert.That(cachedError.Message, Is.EqualTo("Global rejected"));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
                Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.Zero);
            }
        }

        [TestCase]
        public void SizesCooldownPreservesTheCauseWhenTheSharedBreakerIsOpen()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false))
            {
                for (int i = 0; i < RetryCircuitBreaker.DefaultFailureThreshold - 1; i++)
                {
                    RetryCircuitBreaker.RecordFailure();
                }

                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, "Server rejected authentication", shouldRetry: true, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                Assert.That(RetryCircuitBreaker.IsOpen, Is.True);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception cachedError);

                Assert.That(cachedError, Is.SameAs(error));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(1));
                Assert.That(requestor.TestTracer.RelatedErrorEvents.Count, Is.EqualTo(1));
                Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.EqualTo(RetryCircuitBreaker.DefaultFailureThreshold));
            }
        }

        [TestCase]
        public void AnOlderSuccessfulResponseCannotClearANewerSizesFailure()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false))
            {
                Exception newerError = null;
                requestor.EnqueueResponse(new CallbackReadStream(() =>
                    requestor.QueryForFileSizes(new[] { "new" }, CancellationToken.None, out newerError)));
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Newer rejection", isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");

                requestor.QueryForFileSizes(new[] { "old" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "next" }, CancellationToken.None, out Exception cachedError);

                Assert.That(cachedError, Is.SameAs(newerError));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Not.Contain("SizesAuthFailureRecovered"));
            }
        }

        [TestCase]
        public void AnOlderSizesRejectionCannotSuppressAfterANewerSuccess()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false))
            {
                requestor.OnRequest = _ =>
                {
                    requestor.OnRequest = null;
                    requestor.QueryForFileSizes(new[] { "new" }, CancellationToken.None);
                };
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Older rejection", isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");

                requestor.QueryForFileSizes(new[] { "old" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "next" }, CancellationToken.None, out Exception error);

                Assert.That(error, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
                Assert.That(requestor.TestTracer.RelatedEventNames, Does.Not.Contain("SizesAuthFailureCooldown"));
            }
        }

        [TestCase]
        public void PreferredRejectionSurvivesFailedGlobalCredentialRefresh()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Preferred rejected authentication", isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, "Credential refresh failed", shouldRetry: true, isCredentialFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.Unauthorized, "Credential backoff", shouldRetry: true, isCredentialFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception recoveredError);

                Assert.That(error.Message, Is.EqualTo("Preferred rejected authentication"));
                Assert.That(recoveredError, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(4));
                Assert.That(requestor.RequestUris.Last().AbsoluteUri, Is.EqualTo(GlobalCacheServerUrl + "/gvfs/sizes"));
            }
        }

        [TestCase]
        public void SkippingAPreferredCooldownDoesNotAddAGlobalRetry()
        {
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                for (int i = 0; i < 3; i++)
                {
                    requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable, shouldRetry: true);
                }

                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);

                Assert.That(requestor.RequestUris.Count, Is.EqualTo(4));
                Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.EqualTo(2));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreferredProbeReleasesItsReservationBeforeGlobalFallback(bool transportFailure)
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, timeProvider: clock))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(31));
                if (transportFailure)
                {
                    requestor.EnqueueException(new HttpRequestException("Preferred unavailable"));
                }
                else
                {
                    requestor.EnqueueResponse(HttpStatusCode.ServiceUnavailable);
                }

                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.OnRequest = _ =>
                {
                    if (requestor.RequestUris.Count == 4)
                    {
                        requestor.OnRequest = null;
                        requestor.QueryForFileSizes(new[] { "other" }, CancellationToken.None);
                    }
                };
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);

                Assert.That(requestor.RequestUris.Count, Is.EqualTo(5));
                Assert.That(requestor.RequestUris[3].AbsoluteUri, Is.EqualTo(GlobalCacheServerUrl + "/gvfs/sizes"));
                Assert.That(requestor.RequestUris[4].AbsoluteUri, Is.EqualTo(EndpointCacheServerUrl + "/gvfs/sizes"));
            }
        }

        [TestCase]
        public void NonAuthenticationFailureReleasesTheSizesRecoveryProbe()
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false, timeProvider: clock))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Unrelated failure");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);

                Assert.That(error, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
            }
        }

        [TestCase]
        public void OriginSizesFailureUsesItsOwnCooldownAndRecovery()
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 1, endpointOverrides: false, timeProvider: clock))
            {
                requestor.EnqueueResponse(HttpStatusCode.NotFound);
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "Origin rejected authentication", isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception cachedError);
                Assert.That(cachedError, Is.SameAs(error));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));

                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception recoveredError);
                Assert.That(recoveredError, Is.Null);
                Assert.That(requestor.RequestUris.Last().AbsoluteUri, Is.EqualTo("mock://repourl/gvfs/sizes"));
            }
        }

        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.Unauthorized)]
        public void SizesAuthCooldownExpiresAndSuccessReportsRecovery(HttpStatusCode status)
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false, timeProvider: clock))
            {
                requestor.EnqueueResponse(status, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.EnqueueResponse(HttpStatusCode.OK, "[{\"id\":\"abc\",\"size\":12}]");
                List<GitObjectsHttpRequestor.GitObjectSize> sizes = requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);

                Assert.That(error, Is.Null);
                Assert.That(sizes.Single().Size, Is.EqualTo(12));
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
                int recovery = requestor.TestTracer.RelatedEventNames.IndexOf("SizesAuthFailureRecovered");
                Assert.That(recovery, Is.GreaterThanOrEqualTo(0));
                Assert.That(requestor.TestTracer.RelatedEventMetadata[recovery]["SuppressedQueries"], Is.EqualTo(1));
                Assert.That(requestor.TestTracer.RelatedEventMetadata[recovery]["DurationSeconds"], Is.EqualTo(31));
            }
        }

        [TestCase]
        public void FailedSizesAuthRecoveryProbeRenewsTheCooldown()
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(
                maxRetries: 0, endpointOverrides: false, globalCacheServerUrl: "https://user:secret@global-cache:8443/server", timeProvider: clock))
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, "private-response", isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);

                Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
                Assert.That(requestor.TestTracer.RelatedEventNames.Count(name => name == "SizesAuthFailureCooldown"), Is.EqualTo(2));
                List<EventMetadata> cooldowns = requestor.TestTracer.RelatedEventNames
                    .Select((name, index) => new { Name = name, Metadata = requestor.TestTracer.RelatedEventMetadata[index] })
                    .Where(item => item.Name == "SizesAuthFailureCooldown")
                    .Select(item => item.Metadata).ToList();
                Assert.That(cooldowns[0]["State"], Is.EqualTo("Entered"));
                Assert.That(cooldowns[1]["State"], Is.EqualTo("Continued"));
                Assert.That(cooldowns[0]["SuppressedQueries"], Is.EqualTo(0));
                Assert.That(cooldowns[1]["SuppressedQueries"], Is.EqualTo(1));
                foreach (EventMetadata metadata in cooldowns)
                {
                    Assert.That(metadata["Authority"], Is.EqualTo("global-cache:8443"));
                    Assert.That(metadata["StatusCode"], Is.EqualTo(400));
                    Assert.That(metadata["CooldownSeconds"], Is.EqualTo(30));
                    Assert.That(metadata.Keys, Is.EquivalentTo(new[]
                    {
                        "RequestId", "Authority", "StatusCode", "State", "CooldownSeconds", "SuppressedQueries", "DurationSeconds",
                    }));
                }
            }
        }

        [TestCase]
        public void ConcurrentSizesQueriesPermitOnlyOneRecoveryProbe()
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false, timeProvider: clock))
            using (ManualResetEventSlim probeStarted = new ManualResetEventSlim())
            using (ManualResetEventSlim releaseProbe = new ManualResetEventSlim())
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.OnRequest = token =>
                {
                    probeStarted.Set();
                    Assert.That(releaseProbe.Wait(TimeSpan.FromSeconds(10), token), Is.True);
                };
                Task probe = Task.Run(() => requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None));
                try
                {
                    Assert.That(probeStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                    Assert.That(error, Is.Not.Null);
                    Assert.That(requestor.RequestUris.Count, Is.EqualTo(2));
                }
                finally
                {
                    releaseProbe.Set();
                    probe.GetAwaiter().GetResult();
                }
            }
        }

        [TestCase]
        public void CanceledSizesRecoveryProbeReleasesItsReservation()
        {
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (TestGitObjectsHttpRequestor requestor = this.CreateRequestor(maxRetries: 0, endpointOverrides: false, timeProvider: clock))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (CancellationTokenSource probeCancellation = new CancellationTokenSource())
            {
                requestor.EnqueueResponse(HttpStatusCode.BadRequest, isAuthenticationFailure: true);
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None);
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => requestor.QueryForFileSizes(new[] { "abc" }, cancellation.Token));
                clock.Advance(TimeSpan.FromSeconds(31));
                requestor.OnRequest = token =>
                {
                    probeCancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                };
                Assert.Throws<OperationCanceledException>(() => requestor.QueryForFileSizes(new[] { "abc" }, probeCancellation.Token));
                requestor.OnRequest = null;
                requestor.EnqueueResponse(HttpStatusCode.OK, "[]");
                requestor.QueryForFileSizes(new[] { "abc" }, CancellationToken.None, out Exception error);
                Assert.That(error, Is.Null);
                Assert.That(requestor.RequestUris.Count, Is.EqualTo(3));
            }
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
            string endpointCacheServerUrl = EndpointCacheServerUrl,
            TimeProvider timeProvider = null)
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
                new RetryConfig(maxRetries),
                timeProvider);
        }

        private class TestGitObjectsHttpRequestor : GitObjectsHttpRequestor
        {
            private readonly Queue<object> responses = new Queue<object>();

            public TestGitObjectsHttpRequestor(
                Enlistment enlistment,
                CacheServerInfo cacheServer,
                RetryConfig retryConfig,
                TimeProvider timeProvider)
                : this(new MockTracer(), enlistment, cacheServer, retryConfig, timeProvider)
            {
            }

            private TestGitObjectsHttpRequestor(
                MockTracer tracer,
                Enlistment enlistment,
                CacheServerInfo cacheServer,
                RetryConfig retryConfig,
                TimeProvider timeProvider)
                : base(tracer, enlistment, cacheServer, retryConfig, timeProvider ?? TimeProvider.System)
            {
                this.TestTracer = tracer;
                this.RequestUris = new List<Uri>();
            }

            public MockTracer TestTracer { get; }
            public List<Uri> RequestUris { get; }
            public Action<CancellationToken> OnRequest { get; set; }

            public void EnqueueResponse(
                HttpStatusCode statusCode,
                string body = "",
                bool shouldRetry = false,
                bool shouldRecordFailure = true,
                bool isAuthenticationFailure = false,
                bool isCredentialFailure = false)
            {
                this.responses.Enqueue(Tuple.Create(statusCode, body, shouldRetry, shouldRecordFailure, isAuthenticationFailure, isCredentialFailure));
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
                this.OnRequest?.Invoke(cancellationToken);
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
                        new HttpResponseStream(stream, this.RetryConfig.ReadTimeout, cancellationToken, this.TestTracer),
                        message: null,
                        onResponseDisposed: null);
                }

                Tuple<HttpStatusCode, string, bool, bool, bool, bool> response = (Tuple<HttpStatusCode, string, bool, bool, bool, bool>)nextResponse;

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
                    new GitObjectsHttpException(response.Item1, string.IsNullOrEmpty(response.Item2) ? "Test failure" : response.Item2),
                    shouldRetry: response.Item3,
                    message: null,
                    onResponseDisposed: null,
                    shouldRecordFailure: response.Item4,
                    isAuthenticationFailure: response.Item5,
                    isCredentialFailure: response.Item6);
            }
        }

        private class CallbackReadStream : MemoryStream
        {
            private Action beforeRead;

            public CallbackReadStream(Action beforeRead)
                : base(Encoding.UTF8.GetBytes("[]"))
            {
                this.beforeRead = beforeRead;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Action callback = this.beforeRead;
                this.beforeRead = null;
                callback?.Invoke();
                return base.ReadAsync(buffer, cancellationToken);
            }
        }

        private class ThrowingReadStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new IOException("Response body read failed");
            }
        }

        private class CancelingReadStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException("Response body read canceled");
            }
        }

        private class StallingReadStream : MemoryStream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }
        }
    }
}
