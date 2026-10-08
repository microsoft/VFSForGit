using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
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
        public void CredentialFillIsFlaggedAsMayRequireAuth()
        {
            // Only credential fill can trigger an interactive sign-in from the credential manager, so
            // only it is flagged mayRequireAuth=true. GetGitProcess uses that flag to detach git from a
            // hidden console so the prompt is not left behind other windows.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();
            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            dut.TryGetCredentials(tracer, out _, out string error).ShouldEqual(true, "Failed to get credential: " + error);

            gitProcess.MayRequireAuthFor($"{AzureDevOpsUseHttpPathString} credential fill")
                .ShouldEqual(true, "Credential fill should be flagged as possibly requiring auth");

            // Non-credential git commands never prompt, so they must not be flagged.
            gitProcess.MayRequireAuthFor("config --get-urlmatch http mock://repoUrl")
                .ShouldEqual(false, "Non-credential git commands should not be flagged as requiring auth");
        }

        [TestCase]
        public void CredentialApproveIsNotFlaggedAsMayRequireAuth()
        {
            // Credential approve stores an already-obtained credential and never prompts.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();
            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            dut.TryGetCredentials(tracer, out string authString, out string error).ShouldEqual(true, "Failed to get credential: " + error);
            dut.ApproveCredentials(tracer, authString);

            gitProcess.MayRequireAuthFor($"{AzureDevOpsUseHttpPathString} credential approve")
                .ShouldEqual(false, "Credential approve should not be flagged as requiring auth");
        }

        [TestCase]
        public void CredentialRejectIsNotFlaggedAsMayRequireAuth()
        {
            // Credential reject erases a stored credential and never prompts.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();
            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            dut.TryInitializeAndRequireAuth(tracer, out _);

            dut.TryGetCredentials(tracer, out string authString, out string error).ShouldEqual(true, "Failed to get credential: " + error);
            dut.RejectCredentials(tracer, authString);

            gitProcess.MayRequireAuthFor($"{AzureDevOpsUseHttpPathString} credential reject")
                .ShouldEqual(false, "Credential reject should not be flagged as requiring auth");
        }

        [TestCase]
        public void CertificateCredentialFillIsFlaggedAsMayRequireAuth()
        {
            // The certificate credential fill can also trigger an interactive sign-in, so it is flagged
            // mayRequireAuth=true like the URL fill. It is covered directly here because the standard
            // credential flow does not exercise the certificate path.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result("password=certpassword\r\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCertificatePassword(tracer, CertificatePath, out string password, out string error)
                .ShouldEqual(true, "Failed to get certificate password: " + error);

            gitProcess.MayRequireAuthFor("credential fill")
                .ShouldEqual(true, "Certificate credential fill should be flagged as possibly requiring auth");
        }

        [TestCase]
        public void GetGitProcessDetachesFromConsoleOnlyWhenAuthMayBeRequiredAndNoVisibleWindow()
        {
            MockPlatform platform = (MockPlatform)GVFSPlatform.Instance;
            bool originalHasVisibleWindow = platform.HasVisibleWindow;
            try
            {
                GitProcess gitProcess = new GitProcess("git.exe", workingDirectoryRoot: null);

                // No visible console + command may require auth => detach so the prompt is topmost.
                platform.HasVisibleWindow = false;
                using (Process process = gitProcess.GetGitProcess("credential fill", Environment.SystemDirectory, dotGitDirectory: null, useReadObjectHook: false, gitObjectsDirectory: null, usePreCommandHook: false, mayRequireAuth: true))
                {
                    process.StartInfo.CreateNoWindow.ShouldEqual(true, "Should detach when auth may be required and there is no visible console window");
                }

                // Visible console + command may require auth => keep the console so the prompt parents to it.
                platform.HasVisibleWindow = true;
                using (Process process = gitProcess.GetGitProcess("credential fill", Environment.SystemDirectory, dotGitDirectory: null, useReadObjectHook: false, gitObjectsDirectory: null, usePreCommandHook: false, mayRequireAuth: true))
                {
                    process.StartInfo.CreateNoWindow.ShouldEqual(false, "Should keep the console when it is visible");
                }

                // Command cannot require auth => never detach, regardless of console visibility.
                platform.HasVisibleWindow = false;
                using (Process process = gitProcess.GetGitProcess("rev-parse HEAD", Environment.SystemDirectory, dotGitDirectory: null, useReadObjectHook: false, gitObjectsDirectory: null, usePreCommandHook: true, mayRequireAuth: false))
                {
                    process.StartInfo.CreateNoWindow.ShouldEqual(false, "Should never detach when auth cannot be required");
                }

                // Native console probe cannot run => safe fallback assumes a visible console, so do not detach.
                platform.HasVisibleWindow = false;
                platform.ThrowOnConsoleProbe = true;
                using (Process process = gitProcess.GetGitProcess("credential fill", Environment.SystemDirectory, dotGitDirectory: null, useReadObjectHook: false, gitObjectsDirectory: null, usePreCommandHook: false, mayRequireAuth: true))
                {
                    process.StartInfo.CreateNoWindow.ShouldEqual(false, "Should not detach when the console probe cannot run");
                }

                // A caller-supplied console-visibility value is authoritative and bypasses the platform probe
                // (the probe would throw if consulted here).
                platform.ThrowOnConsoleProbe = true;
                platform.HasVisibleWindow = true;
                using (Process process = gitProcess.GetGitProcess("credential fill", Environment.SystemDirectory, dotGitDirectory: null, useReadObjectHook: false, gitObjectsDirectory: null, usePreCommandHook: false, mayRequireAuth: true, hasVisibleConsoleWindow: false))
                {
                    process.StartInfo.CreateNoWindow.ShouldEqual(true, "A caller-supplied false should force detach without probing the platform");
                }
            }
            finally
            {
                platform.HasVisibleWindow = originalHasVisibleWindow;
                platform.ThrowOnConsoleProbe = false;
            }
        }

        [TestCase]
        public void TryGetCredentialLogsConsoleVisibilityWhenCredentialFillFails()
        {
            // The console-visibility telemetry must be recorded on the failure path too, because the
            // hidden-prompt timeout that this diagnostic exists to explain is itself a failure result.
            MockPlatform platform = (MockPlatform)GVFSPlatform.Instance;
            bool originalHasVisibleWindow = platform.HasVisibleWindow;
            try
            {
                platform.HasVisibleWindow = false;

                MockTracer tracer = new MockTracer();
                MockGitProcess gitProcess = new MockGitProcess();
                gitProcess.SetExpectedCommandResult(
                    $"{AzureDevOpsUseHttpPathString} credential fill",
                    () => new GitProcess.Result(string.Empty, "Operation timed out waiting for a response", GitProcess.Result.GenericFailureCode));

                gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out string error, timeoutMs: 5000)
                    .ShouldEqual(false, "Credential fill should report failure when git times out");

                // Assert the logged value, not just the key: the diagnostic is only useful if it records
                // the actual console-visibility state (false here) that explains the hidden-prompt timeout.
                tracer.RelatedWarningEvents.ShouldContain(e => e.Contains("\"hasVisibleConsoleWindow\":false"));

                // The probed value must thread all the way down to the git invocation that GetGitProcess uses.
                gitProcess.InvocationsRun
                    .Single(invocation => invocation.Command == $"{AzureDevOpsUseHttpPathString} credential fill")
                    .HasVisibleConsoleWindow.ShouldEqual(false, "The probed console-visibility value must thread down to the git invocation");
            }
            finally
            {
                platform.HasVisibleWindow = originalHasVisibleWindow;
            }
        }

        [TestCase]
        public void TryGetCertificatePasswordLogsConsoleVisibilityWhenCredentialFillFails()
        {
            // The certificate credential fill stamps the same console-visibility telemetry as the URL fill.
            // It is covered separately because the standard credential flow does not exercise the certificate
            // path, and the value must be present on the failure path where a hidden prompt times out.
            MockPlatform platform = (MockPlatform)GVFSPlatform.Instance;
            bool originalHasVisibleWindow = platform.HasVisibleWindow;
            try
            {
                platform.HasVisibleWindow = false;

                MockTracer tracer = new MockTracer();
                MockGitProcess gitProcess = new MockGitProcess();
                gitProcess.SetExpectedCommandResult(
                    "credential fill",
                    () => new GitProcess.Result(string.Empty, "Operation timed out waiting for a response", GitProcess.Result.GenericFailureCode));

                gitProcess.TryGetCertificatePassword(tracer, CertificatePath, out string password, out string error)
                    .ShouldEqual(false, "Certificate password fill should report failure when git times out");

                tracer.RelatedWarningEvents.ShouldContain(e => e.Contains("\"hasVisibleConsoleWindow\":false"));

                // The probed value must thread all the way down to the git invocation that GetGitProcess uses.
                gitProcess.InvocationsRun
                    .Single(invocation => invocation.Command == "credential fill")
                    .HasVisibleConsoleWindow.ShouldEqual(false, "The probed console-visibility value must thread down to the git invocation");
            }
            finally
            {
                platform.HasVisibleWindow = originalHasVisibleWindow;
            }
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
