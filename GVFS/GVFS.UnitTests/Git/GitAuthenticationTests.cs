using System;
using System.Linq;
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
