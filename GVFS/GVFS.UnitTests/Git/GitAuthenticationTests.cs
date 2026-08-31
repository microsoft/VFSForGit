using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Http;
using NUnit.Framework;

namespace GVFS.UnitTests.Git
{
    [TestFixtureSource(typeof(DataSources), nameof(DataSources.AllBools))]
    public class GitAuthenticationTests
    {
        private const string CertificatePath = "certificatePath";
        private const string AzureDevOpsUseHttpPathString = "-c credential.\"https://dev.azure.com\".useHttpPath=true";

        private readonly bool sslSettingsPresent;

        public GitAuthenticationTests(bool sslSettingsPresent)
        {
            this.sslSettingsPresent = sslSettingsPresent;
        }

        [TestCase]
        public void AuthShouldBackoffAfterFirstRetryFailure()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string authString;
            string error;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");

            dut.RejectCredentials(tracer, authString);
            dut.IsBackingOff.ShouldEqual(false, "Should not backoff after credentials initially rejected");
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration");
            dut.IsBackingOff.ShouldEqual(false, "Should not backoff after successfully getting credentials");

            dut.RejectCredentials(tracer, authString);
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff after rejecting credentials");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(2);
        }

        [TestCase]
        public void BackoffIsNotInEffectAfterSuccess()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string authString;
            string error;

            for (int i = 0; i < 5; ++i)
            {
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get credential on iteration " + i + ": " + error);
                dut.RejectCredentials(tracer, authString);
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration " + i + ": " + error);
                dut.ApproveCredentials(tracer, authString);
                dut.IsBackingOff.ShouldEqual(false, "Should reset backoff after successfully refreshing credentials");
                gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(i+1, $"Should have {i+1} credentials rejection");
                gitProcess.CredentialApprovals["mock://repoUrl"].Count.ShouldEqual(i+1, $"Should have {i+1} credential approvals");
            }
        }

        [TestCase]
        public void ContinuesToBackoffIfTryGetCredentialsFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string authString;
            string error;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");
            dut.RejectCredentials(tracer, authString);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);

            gitProcess.ShouldFail = true;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "Succeeded despite GitProcess returning failure");
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff if failed to get credentials");

            dut.RejectCredentials(tracer, authString);
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff if failed to get credentials");
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);
        }

        [TestCase]
        public void TwoThreadsFailAtOnceStillRetriesOnce()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string authString;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);

            // Simulate a 401 error on two threads
            dut.RejectCredentials(tracer, authString);
            dut.RejectCredentials(tracer, authString);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialRejections["mock://repoUrl"][0].BasicAuthString.ShouldEqual(authString);

            // Both threads should still be able to get a PAT for retry purposes
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "The second thread caused back off when it shouldn't");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);
        }

        [TestCase]
        public void TwoThreadsInterleavingFailuresStillRetriesOnce()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string thread1Auth;
            string thread1AuthRetry;
            string thread2Auth;
            string thread2AuthRetry;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.RejectCredentials(tracer, thread1Auth);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialRejections["mock://repoUrl"][0].BasicAuthString.ShouldEqual(thread1Auth);

            // That thread then retries
            dut.TryGetCredentials(tracer, out thread1AuthRetry, out error).ShouldEqual(true);

            // The second thread fails with the old PAT
            dut.RejectCredentials(tracer, thread2Auth);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1, "Should not have rejected a second time");
            gitProcess.CredentialRejections["mock://repoUrl"][0].BasicAuthString.ShouldEqual(thread1Auth, "Should only have rejected thread1's initial credential");

            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2AuthRetry, out error).ShouldEqual(true, error);
        }

        [TestCase]
        public void TwoThreadsInterleavingFailuresShouldntStompASuccess()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string thread1Auth;
            string thread2Auth;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.RejectCredentials(tracer, thread1Auth);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialRejections["mock://repoUrl"][0].BasicAuthString.ShouldEqual(thread1Auth);

            // That thread then retries and succeeds
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.ApproveCredentials(tracer, thread1Auth);
            gitProcess.CredentialApprovals["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialApprovals["mock://repoUrl"][0].BasicAuthString.ShouldEqual(thread1Auth);

            // If the second thread fails with the old PAT, it shouldn't stomp the new PAT
            dut.RejectCredentials(tracer, thread2Auth);
            gitProcess.CredentialRejections["mock://repoUrl"].Count.ShouldEqual(1);

            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);
            thread2Auth.ShouldEqual(thread1Auth, "The second thread stomp the first threads good auth string");
        }

        [TestCase]
        public void DontDoubleStoreExistingCredential()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            string authString;
            dut.TryGetCredentials(tracer, out authString, out _).ShouldBeTrue();
            dut.ApproveCredentials(tracer, authString);
            dut.ApproveCredentials(tracer, authString);
            dut.ApproveCredentials(tracer, authString);
            dut.ApproveCredentials(tracer, authString);
            dut.ApproveCredentials(tracer, authString);

            gitProcess.CredentialApprovals["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialRejections.Count.ShouldEqual(0);
            gitProcess.StoredCredentials.Count.ShouldEqual(1);
            gitProcess.StoredCredentials.Single().Key.ShouldEqual("mock://repoUrl");
        }

        [TestCase]
        public void DontStoreDifferentCredentialFromCachedValue()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            // Get and store an initial value that will be cached
            string authString;
            dut.TryGetCredentials(tracer, out authString, out _).ShouldBeTrue();
            dut.ApproveCredentials(tracer, authString);

            // Try and store a different value from the one that is cached
            dut.ApproveCredentials(tracer, "different value");

            gitProcess.CredentialApprovals["mock://repoUrl"].Count.ShouldEqual(1);
            gitProcess.CredentialRejections.Count.ShouldEqual(0);
            gitProcess.StoredCredentials.Count.ShouldEqual(1);
            gitProcess.StoredCredentials.Single().Key.ShouldEqual("mock://repoUrl");
        }

        [TestCase]
        public void RejectionShouldNotBeSentIfUnderlyingTokenHasChanged()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            // Get and store an initial value that will be cached
            string authString;
            dut.TryGetCredentials(tracer, out authString, out _).ShouldBeTrue();
            dut.ApproveCredentials(tracer, authString);

            // Change the underlying token
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result("username=username\r\npassword=password" + Guid.NewGuid() + "\r\n", string.Empty, GitProcess.Result.SuccessCode));

            // Try and reject it. We should get a new token, but without forwarding the rejection to the
            // underlying credential store
            dut.RejectCredentials(tracer, authString);
            dut.TryGetCredentials(tracer, out var newAuthString, out _).ShouldBeTrue();
            newAuthString.ShouldNotEqual(authString);
            gitProcess.CredentialRejections.ShouldBeEmpty();
        }

        [TestCase]
        public void TryGetCredentialsBeforeInitializationDoesNotThrow()
        {
            // Regression test for a mount crash: when a cache server is configured,
            // mount starts virtualization before the background auth/config query
            // finishes initializing. A directory enumeration then calls
            // TryGetCredentials while IsAnonymous has already been set false but
            // initialization has not completed. The previous behavior threw an
            // InvalidOperationException straight into the ProjFS callback, crashing
            // the mount process. It must instead fail gracefully (retryable).
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            // Keep the wait short so an uninitialized instance reports failure
            // quickly instead of blocking for the full background timeout.
            dut.InitializationWaitTimeoutMs = 10;

            string authString = null;
            string error = null;
            bool result = true;

            Assert.DoesNotThrow(() => result = dut.TryGetCredentials(tracer, out authString, out error));

            result.ShouldBeFalse("TryGetCredentials should fail (not throw) before initialization completes");
            error.ShouldNotBeNull("A retryable error message should be returned");
        }

        [TestCase]
        public void TryGetCredentialsWaitsForBackgroundInitializationThenSucceeds()
        {
            // A caller that arrives before initialization completes should block
            // until initialization finishes, then return the fetched credentials -
            // this is the correct behavior for the background cache-server auth path.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.InitializationWaitTimeoutMs = 30_000;

            string authString = null;
            string error = null;
            bool result = false;

            using (ManualResetEventSlim consumerStarted = new ManualResetEventSlim(false))
            {
                Task consumer = Task.Run(() =>
                {
                    consumerStarted.Set();
                    result = dut.TryGetCredentials(tracer, out authString, out error);
                });

                // Ensure the consumer has begun waiting on initialization before we
                // complete it, so we exercise the wait path rather than a no-op.
                consumerStarted.Wait();
                Thread.Sleep(50);

                dut.TryInitializeAndRequireAuth(tracer, out _);

                consumer.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("Consumer should unblock once initialization completes");
            }

            result.ShouldBeTrue("TryGetCredentials should succeed after initialization: " + error);
            authString.ShouldNotBeNull("A credential string should be returned");
        }

        [TestCase]
        public void AuthIsNotAnonymousBeforeInitialization()
        {
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            dut.IsAnonymous.ShouldEqual(false, "Auth must not report anonymous before a probe has proven the server allows it");
        }

        [TestCase]
        public void AnonymousConfigProbeSuccessLeavesAuthAnonymous()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();
            MockGVFSConfigRequestor requestor = MockGVFSConfigRequestor.AnonymousSucceeds();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.ConfigRequestorFactory = (t, e, r) => requestor;

            dut.TryInitializeAndQueryGVFSConfig(tracer, null, new RetryConfig(), out _, out _, out bool isAuthFailure)
                .ShouldEqual(true, "An anonymous server should initialize successfully");

            dut.IsAnonymous.ShouldEqual(true, "A successful unauthenticated probe means the server allows anonymous access");
            isAuthFailure.ShouldEqual(false, "An anonymous success is not an auth failure");
            requestor.QueryCount.ShouldEqual(1, "An anonymous server needs only the single unauthenticated query");
        }

        [TestCase]
        public void UnauthorizedConfigProbeRequiresAuthentication()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.ConfigRequestorFactory = (t, e, r) => MockGVFSConfigRequestor.RequiresAuthentication();

            dut.TryInitializeAndQueryGVFSConfig(tracer, null, new RetryConfig(), out _, out _, out _);

            dut.IsAnonymous.ShouldEqual(false, "A 401 from the unauthenticated probe means credentials are required");
            dut.TryGetCredentials(tracer, out string authString, out _).ShouldEqual(true, "Credentials should be available after a 401 probe");
            authString.ShouldNotBeNull("A credential should have been fetched");
        }

        /// <summary>
        /// A config query that fails for a reason unrelated to authentication - a
        /// timeout, a 5xx, or a socket error - says nothing about whether the server
        /// allows anonymous access. Treating it as anonymous is unrecoverable:
        /// HttpRequestor.SendRequest omits the Authorization header and never calls
        /// TryGetCredentials while IsAnonymous is true, the Azure DevOps cache server
        /// answers 400, a 400 is not retryable, and initialization is already latched
        /// so the probe never runs again. Mount proceeds when a cache server is
        /// configured, so the repo stays mounted but cannot hydrate or enumerate.
        /// </summary>
        [TestCase]
        public void IndeterminateConfigProbeDoesNotLeaveAuthAnonymous()
        {
            foreach (HttpStatusCode? status in new HttpStatusCode?[]
            {
                HttpStatusCode.RequestTimeout,
                HttpStatusCode.InternalServerError,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.BadRequest,
                null,
            })
            {
                MockTracer tracer = new MockTracer();
                MockGitProcess gitProcess = this.GetGitProcess();

                GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
                dut.ConfigRequestorFactory = (t, e, r) => MockGVFSConfigRequestor.Indeterminate(status);

                dut.TryInitializeAndQueryGVFSConfig(tracer, null, new RetryConfig(), out _, out _, out bool isAuthFailure)
                    .ShouldEqual(false, $"A config query that failed with {status?.ToString() ?? "no response"} should report failure");

                dut.IsAnonymous.ShouldEqual(
                    false,
                    $"An indeterminate config probe ({status?.ToString() ?? "no response"}) must not leave auth in anonymous mode");

                isAuthFailure.ShouldEqual(false, "An indeterminate failure is not an authentication failure");
            }
        }

        [TestCase]
        public void IndeterminateConfigProbeStillAuthenticatesLaterRequests()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.ConfigRequestorFactory = (t, e, r) => MockGVFSConfigRequestor.Indeterminate(HttpStatusCode.RequestTimeout);

            dut.TryInitializeAndQueryGVFSConfig(tracer, null, new RetryConfig(), out _, out _, out _);

            // Mount proceeds past this failure when a cache server is configured, so
            // object downloads and directory enumeration must still be able to
            // authenticate rather than silently issuing unauthenticated requests.
            dut.IsAnonymous.ShouldEqual(false, "Later requests must send an Authorization header");
            dut.TryGetCredentials(tracer, out string authString, out string error)
                .ShouldEqual(true, "Credentials should still be obtainable after an indeterminate probe: " + error);
            authString.ShouldNotBeNull("A credential should have been fetched");
        }

        private MockGitProcess GetGitProcess()
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.UserName", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.Password", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            if (this.sslSettingsPresent)
            {
                gitProcess.SetExpectedCommandResult("config --get-urlmatch http mock://repoUrl", () => new GitProcess.Result($"http.sslCert {CertificatePath}\nhttp.sslCertPasswordProtected true\n\n", string.Empty, GitProcess.Result.SuccessCode));
            }
            else
            {
                gitProcess.SetExpectedCommandResult("config --get-urlmatch http mock://repoUrl", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            }

            int approvals = 0;
            int rejections = 0;
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result("username=username\r\npassword=password" + rejections + "\r\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential approve",
                () =>
                {
                    approvals++;
                    return new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode);
                });

            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential reject",
                () =>
                {
                    rejections++;
                    return new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode);
                });
            return gitProcess;
        }
    }
}
