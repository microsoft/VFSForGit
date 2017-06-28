using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Physical.Git
{
    [TestFixture]
    public class GitAuthenticationTests
    {
        [TestCase]
        public void ShouldOnlyRetryAuthOnce()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string authString;
            string error;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");
            
            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true, "Should retry once");

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration");

            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(false, "Should not retry more than once");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
        }

        [TestCase]
        public void CanRetryManyTimesIfTheresSomeSuccess()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string authString;
            string error;
            
            for (int i = 0; i < 5; ++i)
            {
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get credential on iteration " + i + ": " + error);
                
                dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true, "Did not retry after revoke on iteration: " + i);
                
                dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to retry getting credential on iteration " + i + ": " + error);
                
                dut.ConfirmCredentialsWorked(authString);
            }
        }

        [TestCase]
        public void DoesNotRetryIfTryGetCredentialsFails()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string authString;
            string error;
            
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get initial credential");

            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true, "Should retry once");

            gitProcess.ShouldFail = true;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "Succeeded despite GitProcess returning failure");

            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(false, "Should not retry if GitProcess fails after retry");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "TryGetCredential should not succeed during backoff");
        }

        [TestCase]
        public void GitProcessFailuresAreRetried()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string authString;
            string error;

            gitProcess.ShouldFail = true;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(false, "Succeeded despite GitProcess returning failure");

            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true, "Should retry once");

            gitProcess.ShouldFail = false;

            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "Failed to get credential on retry");
        }

        [TestCase]
        public void TwoThreadsFailAtOnceStillRetriesOnce()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);
            
            GitAuthentication dut = new GitAuthentication(gitProcess);
            
            string authString;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);

            // Simulate a 401 error on two threads
            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true);
            dut.RevokeAndCheckCanRetry(authString).ShouldEqual(true, "The first thread is denying the second thread a chance to retry.");
            
            // Both threads should still be able to get a PAT for retry purposes
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true, "The second thread caused back off when it shouldn't");
            dut.TryGetCredentials(tracer, out authString, out error).ShouldEqual(true);
        }

        [TestCase]
        public void TwoThreadsInterleavingFailuresStillRetriesOnce()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string thread1Auth;
            string thread2Auth;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.RevokeAndCheckCanRetry(thread1Auth).ShouldEqual(true);

            // That thread then retries            
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            
            // The second thread fails with the old PAT
            dut.RevokeAndCheckCanRetry(thread2Auth).ShouldEqual(true);
            
            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);
        }

        [TestCase]
        public void TwoThreadsInterleavingFailuresShouldntStompASuccess()
        {
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);

            GitAuthentication dut = new GitAuthentication(gitProcess);

            string thread1Auth;
            string thread2Auth;
            string error;

            // Populate an initial PAT on two threads
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);

            // Simulate a 401 error on one threads
            dut.RevokeAndCheckCanRetry(thread1Auth).ShouldEqual(true);

            // That thread then retries and succeeds
            dut.TryGetCredentials(tracer, out thread1Auth, out error).ShouldEqual(true);
            dut.ConfirmCredentialsWorked(thread1Auth);

            // If the second thread fails with the old PAT, it shouldn't stomp the new PAT
            dut.RevokeAndCheckCanRetry(thread2Auth).ShouldEqual(true);

            // The second thread should be able to get a PAT
            dut.TryGetCredentials(tracer, out thread2Auth, out error).ShouldEqual(true);
            thread2Auth.ShouldEqual(thread1Auth, "The second thread stomp the first threads good auth string");
        }

        private class MockGitProcess : GitProcess
        {
            private int revocations = 0;

            public MockGitProcess(Enlistment enlistment) : base(enlistment)
            {
            }

            public bool ShouldFail { get; set; }

            public override void RevokeCredential()
            {
                this.revocations++;
            }

            public override bool TryGetCredentials(ITracer tracer, out string username, out string password)
            {
                if (this.ShouldFail)
                {
                    username = null;
                    password = null;
                    return false;
                }

                username = "username";
                password = "password" + this.revocations;
                return true;
            }
        }
    }
}
