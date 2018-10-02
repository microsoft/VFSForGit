using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitAuthenticationTests
    {
        [TestCase]
        public void AuthShouldBackoffAfterFirstRetryFailure()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            string authString;
            string error;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");            

            dut.Revoke(authString);
            dut.IsBackingOff.ShouldEqual(false, "Should not backoff after credentials initially revoked");

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration");
            dut.IsBackingOff.ShouldEqual(false, "Should not backoff after successfully getting credentials");

            dut.Revoke(authString);
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff after revoking credentials");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
        }

        [TestCase]
        public void BackoffIsNotInEffectAfterSuccess()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            string authString;
            string error;
        
            for (int i = 0; i < 5; ++i)
            {
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get credential on iteration " + i + ": " + error);            
                dut.Revoke(authString);            
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration " + i + ": " + error);
                dut.ConfirmCredentialsWorked(authString);
                dut.IsBackingOff.ShouldEqual(false, "Should reset backoff after successfully refreshing credentials");
            }
        }

        [TestCase]
        public void ContinuesToBackoffIfTryGetCredentialsFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            string authString;
            string error;
        
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");
            dut.Revoke(authString);

            gitProcess.ShouldFail = true;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "Succeeded despite GitProcess returning failure");
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff if failed to get credentials");

            dut.Revoke(authString);
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
            dut.IsBackingOff.ShouldEqual(true, "Should continue to backoff if failed to get credentials");
        }

        [TestCase]
        public void GitProcessFailuresAreRetried()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            string authString;
            string error;

            gitProcess.ShouldFail = true;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "Succeeded despite GitProcess returning failure");

            // Reboke should be a no-op as valid credentials have not been stored
            dut.Revoke(authString);
            dut.IsBackingOff.ShouldEqual(false, "Should not backoff if there were no credentials to revoke");

            gitProcess.ShouldFail = false;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get credential on retry");
        }

        [TestCase]
        public void TwoThreadsFailAtOnceStillRetriesOnce()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");
            
            string authString;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);

            // Simulate a 401 error on two threads
            dut.Revoke(authString);
            dut.Revoke(authString);
        
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

            string thread1Auth;
            string thread2Auth;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.Revoke(thread1Auth);

            // That thread then retries            
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            
            // The second thread fails with the old PAT
            dut.Revoke(thread2Auth);
        
            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true, error);
        }

        [TestCase]
        public void TwoThreadsInterleavingFailuresShouldntStompASuccess()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = this.GetGitProcess();

            GitAuthentication dut = new GitAuthentication(gitProcess, "mock://repoUrl");

            string thread1Auth;
            string thread2Auth;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.Revoke(thread1Auth);

            // That thread then retries and succeeds
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.ConfirmCredentialsWorked(thread1Auth);

            // If the second thread fails with the old PAT, it shouldn't stomp the new PAT
            dut.Revoke(thread2Auth);

            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);
            thread2Auth.ShouldEqual(thread1Auth, "The second thread stomp the first threads good auth string");
        }

        private MockGitProcess GetGitProcess()
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.UserName", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.FunctionalTests.Password", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            int revocations = 0;
            gitProcess.SetExpectedCommandResult(
                "-c credential.useHttpPath=true credential fill",
                () => new GitProcess.Result("username=username\r\npassword=password" + revocations + "\r\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.SetExpectedCommandResult(
                "credential reject",
                () =>
                {
                    revocations++;
                    return new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode);
                });
            return gitProcess;
        }
    }
}
