using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
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
        [TestCase(HttpStatusCode.Unauthorized, "The credential is invalid.", true)]
        [TestCase(HttpStatusCode.Unauthorized, "Authorization failed for repository 00000000-0000-0000-0000-000000000001", true)]
        [TestCase(HttpStatusCode.Redirect, "Sign in to continue.", false)]
        [TestCase(HttpStatusCode.BadRequest, HttpRequestor.CacheServerAuthRequiredBadRequestMessage, false)]
        public void AuthenticationFailuresDoNotInferCredentialRenewal(HttpStatusCode statusCode, string serverMessage, bool shouldRetry)
        {
            const string RepoUrl = "mock://repoUrl";
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "config ",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode),
                matchPrefix: true);
            gitProcess.SetExpectedCommandResult(
                "-c credential.\"https://dev.azure.com\".useHttpPath=true credential fill",
                () => new GitProcess.Result("username=test\r\npassword=unchanged\r\n", string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult(
                "-c credential.\"https://dev.azure.com\".useHttpPath=true credential reject",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            using MockTracer tracer = new MockTracer();
            GitAuthentication authentication = new GitAuthentication(gitProcess, RepoUrl);
            authentication.TryInitializeAndRequireAuth(tracer, out string error).ShouldEqual(true, error);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(
                @"mock:\repo", RepoUrl, @"mock:\git", gitProcess, authentication);
            using TestHttpRequestor requestor = new TestHttpRequestor(tracer, enlistment, new ErrorResponseHandler(statusCode, serverMessage));

            for (int rejection = 1; rejection <= 2; ++rejection)
            {
                using GitEndPointResponseData response = requestor.Send();
                response.Error.Message.ShouldEqual(
                    $"Server returned error code {(int)statusCode} ({statusCode}). Original error message from server: {serverMessage}");
                response.StatusCode.ShouldEqual(statusCode);
                response.ShouldRetry.ShouldEqual(shouldRetry);
                gitProcess.CredentialRejections[RepoUrl].Count.ShouldEqual(rejection);
                authentication.IsBackingOff.ShouldEqual(rejection == 2);
            }
        }

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

        [TestCase("", "")]
        [TestCase("cache-01_test.example", "cache-01_test.example")]
        [TestCase("cache:\r\nname?secret", "cache___name_secret")]
        public void CacheNameTelemetryContainsOnlyIdentityCharacters(string input, string expected)
        {
            HttpRequestor.GetCacheNameForTelemetry(input).ShouldEqual(expected);
        }

        [TestCase]
        public void CacheNameTelemetryHasABoundedLength()
        {
            HttpRequestor.GetCacheNameForTelemetry(new string('a', 1000)).ShouldEqual(new string('a', 128));
        }

        private class TestHttpRequestor : HttpRequestor
        {
            public TestHttpRequestor(MockTracer tracer, MockGVFSEnlistment enlistment, HttpMessageHandler handler)
                : base(tracer, new RetryConfig(), enlistment, handler)
            {
            }

            public GitEndPointResponseData Send()
            {
                return this.SendRequest(
                    GetNewRequestId(), new Uri("https://cache.example.com/objects"), HttpMethod.Get, null, CancellationToken.None);
            }
        }

        private class ErrorResponseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode statusCode;
            private readonly string serverMessage;

            public ErrorResponseHandler(HttpStatusCode statusCode, string serverMessage)
            {
                this.statusCode = statusCode;
                this.serverMessage = serverMessage;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.Authorization.Scheme.ShouldEqual("Basic");
                return Task.FromResult(new HttpResponseMessage(this.statusCode) { Content = new StringContent(this.serverMessage) });
            }
        }
    }
}
