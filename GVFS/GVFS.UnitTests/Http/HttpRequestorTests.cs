using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Http
{
    [TestFixture]
    public class HttpRequestorTests
    {
        [TestCase]
        public void Unauthorized401RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Unauthorized, responseBody: null)
                .ShouldEqual(true, "A 401 is a definitive auth failure and must reject credentials");
        }

        [TestCase]
        public void Redirect302RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Redirect, responseBody: null)
                .ShouldEqual(true, "A 302 is the Azure DevOps sign-in redirect and must reject credentials");
        }

        [TestCase]
        public void BadRequest400WithAuthRequiredMessageRejectsCredentials()
        {
            // The GVFS cache server returns a 400 (instead of a 401) when the request carried
            // no parseable Basic Authorization header. That single 400 genuinely means
            // "authentication required", so it must reject credentials. We recognize it by the
            // cache server's response body.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    HttpRequestor.CacheServerAuthRequiredBadRequestMessage)
                .ShouldEqual(true, "A 400 whose body is the cache server's auth-required message must reject credentials");
        }

        [TestCase]
        public void BadRequest400AuthRequiredMessageMatchIsCaseInsensitiveAndSubstring()
        {
            // The match is a case-insensitive substring so it stays robust if the server
            // wraps or prefixes the text.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    "Error: a valid basic authorization header is required. (request 123)")
                .ShouldEqual(true, "The auth-required message match must be a case-insensitive substring");
        }

        [TestCase]
        public void BadRequest400WithNonAuthBodyDoesNotRejectCredentials()
        {
            // The storm case: a corrupt placeholder SHA makes the cache server return a 400
            // with an "Invalid ObjectId" body. That is NOT an auth failure and must not erase
            // a valid credential.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    "Error processing GVFS request: Invalid ObjectId in the URI.")
                .ShouldEqual(false, "A non-auth 400 (e.g. invalid object id) must NOT reject credentials");
        }

        [TestCase]
        public void BadRequest400WithNullBodyDoesNotRejectCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.BadRequest, responseBody: null)
                .ShouldEqual(false, "A 400 with no body must NOT reject credentials");
        }

        [TestCase]
        public void CommonNonAuthStatusesDoNotRejectCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.NotFound, responseBody: null)
                .ShouldEqual(false, "A 404 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.InternalServerError, responseBody: null)
                .ShouldEqual(false, "A 500 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.RequestTimeout, responseBody: null)
                .ShouldEqual(false, "A 408 must NOT reject credentials");
        }

        [TestCase]
        public void AuthorityForTelemetryExcludesCredentialsAndRequestPath()
        {
            Uri uri = new Uri("https://alice:secret@cache.example.com:8443/private/path?token=sensitive#fragment");

            HttpRequestor.GetAuthorityForTelemetry(uri).ShouldEqual("cache.example.com:8443");
        }

        private const string RepoUrl = "mock://repoUrl";
        private const string AzureDevOpsUseHttpPathString = "-c credential.\"https://dev.azure.com\".useHttpPath=true";

        [TestCase]
        public void SendRequestReleasesConnectionBeforeCredentialRejectWhenEnabled()
        {
            this.RunConnectionReleaseTest(releaseEarly: true, expectReleasedDuringReject: true);
        }

        [TestCase]
        public void SendRequestHoldsConnectionDuringCredentialRejectWhenDisabled()
        {
            this.RunConnectionReleaseTest(releaseEarly: false, expectReleasedDuringReject: false);
        }

        [TestCase]
        public void SendRequestDoesNotDoubleReleaseConnectionWhenRejectCanceled()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess();
            GitAuthentication authentication = CreateInitializedAuthentication(tracer, gitProcess);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(gitProcess, authentication);

            using (ManualResetEventSlim reached = new ManualResetEventSlim(false))
            using (ManualResetEventSlim block = new ManualResetEventSlim(false))
            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "unauthorized"))
            using (TestingHttpRequestor requestor = new TestingHttpRequestor(tracer, new RetryConfig(), enlistment, handler, releaseEarly: true))
            {
                int before = HttpRequestor.AvailableConnectionCount;
                gitProcess.InvokeReachedBlock = reached;
                gitProcess.BlockInvokeUntilSignaled = block;

                Exception caught = null;
                Thread worker = new Thread(() =>
                {
                    try
                    {
                        using (requestor.Send(cts.Token))
                        {
                        }
                    }
                    catch (Exception e)
                    {
                        caught = e;
                    }
                });
                worker.IsBackground = true;
                worker.Start();

                reached.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true, "The reject leg should have started a git invocation");
                HttpRequestor.AvailableConnectionCount.ShouldEqual(before, "The connection slot should have been released before the reject leg ran");

                cts.Cancel();
                worker.Join(TimeSpan.FromSeconds(5)).ShouldEqual(true, "Cancellation should have unblocked the reject leg promptly");

                caught.ShouldNotBeNull("Expected the canceled request to throw");
                (caught is OperationCanceledException).ShouldEqual(true, "Expected an OperationCanceledException, got: " + caught);

                // The key invariant: the early release plus the canceled reject must not
                // double-release the process-wide connection permit.
                HttpRequestor.AvailableConnectionCount.ShouldEqual(before, "The connection slot must be released exactly once, not double-released");
            }
        }

        [TestCase]
        public void CredentialTimeoutDoesNotFallBackToGlobalEndpoint()
        {
            using (GitEndPointResponseData response = new GitEndPointResponseData(
                HttpStatusCode.Unauthorized,
                new GitObjectsHttpException(HttpStatusCode.Unauthorized, "credential timeout"),
                shouldRetry: false,
                message: null,
                onResponseDisposed: null,
                credentialFetchTimedOut: true))
            {
                GitObjectsHttpRequestor.ShouldFallBackToGlobalEndpoint(
                    hasFallbackEndPoint: true,
                    useFallbackEndPoint: false,
                    response)
                    .ShouldBeFalse("A second endpoint uses the same timed-out credential provider");
            }
        }

        private static MockGitProcess CreateGitProcess()
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.UserName", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.Password", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config --get-urlmatch http mock://repoUrl", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            // HttpRequestor reads these once (on the first instance constructed in the process)
            // during its connection-limit / flag initialization. Register them so the read does
            // not fault the mock, regardless of test ordering.
            gitProcess.SetExpectedCommandResult("config gvfs.max-http-connections", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.release-connection-before-credential-reject", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            int rejections = 0;
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result("username=username\r\npassword=password" + rejections + "\r\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential approve",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential reject",
                () =>
                {
                    rejections++;
                    return new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode);
                });

            return gitProcess;
        }

        private static GitAuthentication CreateInitializedAuthentication(MockTracer tracer, MockGitProcess gitProcess)
        {
            GitAuthentication authentication = new GitAuthentication(gitProcess, RepoUrl);
            authentication.TryInitializeAndRequireAuth(tracer, out _);

            // Populate the credential cache so SendRequest attaches auth and reaches the reject leg.
            authentication.TryGetCredentials(tracer, out _, out _).ShouldBeTrue();

            // Force the non-anonymous path; production determines this by probing the server.
            authentication.SetIsAnonymousForTesting(false);

            return authentication;
        }

        private void RunConnectionReleaseTest(bool releaseEarly, bool expectReleasedDuringReject)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess();
            GitAuthentication authentication = CreateInitializedAuthentication(tracer, gitProcess);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(gitProcess, authentication);

            using (ManualResetEventSlim reached = new ManualResetEventSlim(false))
            using (ManualResetEventSlim block = new ManualResetEventSlim(false))
            using (StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "unauthorized"))
            using (TestingHttpRequestor requestor = new TestingHttpRequestor(tracer, new RetryConfig(), enlistment, handler, releaseEarly))
            {
                int before = HttpRequestor.AvailableConnectionCount;
                gitProcess.InvokeReachedBlock = reached;
                gitProcess.BlockInvokeUntilSignaled = block;

                GitEndPointResponseData response = null;
                Thread worker = new Thread(() => response = requestor.Send(CancellationToken.None));
                worker.IsBackground = true;
                worker.Start();

                reached.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true, "The reject leg should have started a git invocation");

                int duringReject = HttpRequestor.AvailableConnectionCount;
                if (expectReleasedDuringReject)
                {
                    duringReject.ShouldEqual(before, "The connection slot should be released before the reject leg runs");
                }
                else
                {
                    duringReject.ShouldEqual(before - 1, "The connection slot should still be held during the reject leg");
                }

                block.Set();
                worker.Join(TimeSpan.FromSeconds(5)).ShouldEqual(true, "The request should complete after the reject leg unblocks");

                response.ShouldNotBeNull("Expected a response");
                response.HasErrors.ShouldEqual(true, "Expected a 401 error response");
                response.Dispose();

                HttpRequestor.AvailableConnectionCount.ShouldEqual(before, "The connection slot should be fully released after completion");
            }
        }

        private sealed class TestingHttpRequestor : HttpRequestor
        {
            private readonly bool releaseEarly;

            public TestingHttpRequestor(ITracer tracer, RetryConfig retryConfig, Enlistment enlistment, HttpMessageHandler handler, bool releaseEarly)
                : base(tracer, retryConfig, enlistment, handler)
            {
                this.releaseEarly = releaseEarly;
            }

            protected override int CredentialTimeoutMs => GitAuthentication.BackgroundCredentialTimeoutMs;

            protected override bool ShouldReleaseConnectionBeforeCredentialReject => this.releaseEarly;

            public GitEndPointResponseData Send(CancellationToken cancellationToken)
            {
                return this.SendRequest(
                    GetNewRequestId(),
                    new Uri("https://mock.gvfs/gvfs/objects"),
                    HttpMethod.Get,
                    requestContent: null,
                    cancellationToken: cancellationToken);
            }
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode statusCode;
            private readonly string body;

            public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
            {
                this.statusCode = statusCode;
                this.body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = new HttpResponseMessage(this.statusCode)
                {
                    Content = new StringContent(this.body),
                };

                return Task.FromResult(response);
            }
        }
    }
}
