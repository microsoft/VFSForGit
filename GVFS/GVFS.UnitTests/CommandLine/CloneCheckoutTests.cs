using GVFS.CommandLine;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CloneCheckoutTests
    {
        private const string Branch = "test-branch";
        private const string CheckoutCommand = "checkout -f " + Branch;

        [TestCase("", true)]
        [TestCase("Already on test-branch", true)]
        [TestCase(" \nAlready on test-branch\n", true)]
        [TestCase("", false)]
        [TestCase("Already on test-branch", false)]
        public void FailedCheckoutWithFilteredDiagnosticsPreservesModePolicy(string errors, bool optimisticCheckout)
        {
            string error = CloneVerb.GetCheckoutError(new GitProcess.Result("", errors, 128), Branch, optimisticCheckout);

            error.ShouldEqual(optimisticCheckout
                ? "Could not complete checkout of branch: test-branch, git checkout exited with code 128."
                : null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FinalCheckoutErrorPreservesGitDiagnostic(bool optimisticCheckout)
        {
            string error = CloneVerb.GetCheckoutError(
                new GitProcess.Result("", "fatal: missing tree", 128), Branch, optimisticCheckout);

            error.ShouldEqual("Could not complete checkout of branch: test-branch, fatal: missing tree" + System.Environment.NewLine);
            CloneVerb.GetCheckoutError(new GitProcess.Result("", "", 0), Branch, optimisticCheckout).ShouldEqual(null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SuccessfulCheckoutDoesNotDownload(bool optimisticCheckout)
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(CheckoutCommand, () => new GitProcess.Result("", "Already on test-branch", 0));
            int downloads = 0;

            GitProcess.Result result = CloneVerb.CheckoutWithCommitRetry(
                new MockTracer(), git, Branch, optimisticCheckout, () => { ++downloads; return true; }, out bool failed);

            result.ExitCodeIsSuccess.ShouldBeTrue();
            failed.ShouldBeFalse();
            downloads.ShouldEqual(0);
            git.CommandsRun.Count.ShouldEqual(1);
        }

        [TestCase("fatal: unable to read tree 1111111111111111111111111111111111111111\n", true)]
        [TestCase("error: unable to read tree (1111111111111111111111111111111111111111)\r\n", true)]
        [TestCase("unable to read tree 1111111111111111111111111111111111111111", true)]
        [TestCase("warning: test\nfatal: bad tree object 1111111111111111111111111111111111111111\n", true)]
        [TestCase("fatal: unable to read tree 1111111111111111111111111111111111111111\n", false)]
        public void MissingTreeDownloadsAndRetriesOnce(string errors, bool optimisticCheckout)
        {
            MockGitProcess git = new MockGitProcess();
            int attempts = 0;
            int downloads = 0;
            git.SetExpectedCommandResult(
                CheckoutCommand,
                () => ++attempts == 1
                    ? new GitProcess.Result("", errors, 128)
                    : new GitProcess.Result("", "", 0));

            GitProcess.Result result = CloneVerb.CheckoutWithCommitRetry(
                new MockTracer(), git, Branch, optimisticCheckout, () => { ++downloads; return true; }, out bool failed);

            result.ExitCodeIsSuccess.ShouldBeTrue();
            failed.ShouldBeFalse();
            downloads.ShouldEqual(1);
            attempts.ShouldEqual(2);
        }

        [TestCase("fatal: Unable to create 'mock:/repo/.git/index.lock': File exists.", true)]
        [TestCase("fatal: cannot create directory 'unable to read tree': Permission denied", true)]
        [TestCase("error: unable to read sha1 file of file (1111111111111111111111111111111111111111)", true)]
        [TestCase("error: Could not read 1111111111111111111111111111111111111111", true)]
        [TestCase("", true)]
        [TestCase("fatal: bad tree object 1111111111111111111111111111111111111111", false)]
        [TestCase("unable to read tree 1111111111111111111111111111111111111111", false)]
        public void OtherFailuresDoNotDownload(string errors, bool optimisticCheckout)
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(CheckoutCommand, () => new GitProcess.Result("", errors, 128));
            int downloads = 0;

            GitProcess.Result result = CloneVerb.CheckoutWithCommitRetry(
                new MockTracer(), git, Branch, optimisticCheckout, () => { ++downloads; return true; }, out bool failed);

            result.Errors.ShouldEqual(errors);
            result.ExitCode.ShouldEqual(128);
            failed.ShouldBeFalse();
            downloads.ShouldEqual(0);
            git.CommandsRun.Count.ShouldEqual(1);
        }

        [TestCase]
        public void FailedDownloadDoesNotRetryCheckout()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                CheckoutCommand,
                () => new GitProcess.Result("", "fatal: unable to read tree 1111111111111111111111111111111111111111", 128));
            int downloads = 0;

            CloneVerb.CheckoutWithCommitRetry(
                new MockTracer(), git, Branch, true, () => { ++downloads; return false; }, out bool failed);

            failed.ShouldBeTrue();
            downloads.ShouldEqual(1);
            git.CommandsRun.Count.ShouldEqual(1);
        }

        [TestCase]
        public void RetryFailureIsReturnedWithoutAnotherDownload()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                CheckoutCommand,
                () => new GitProcess.Result("", "fatal: unable to read tree 1111111111111111111111111111111111111111", 128));
            int downloads = 0;

            GitProcess.Result result = CloneVerb.CheckoutWithCommitRetry(
                new MockTracer(), git, Branch, true, () => { ++downloads; return true; }, out bool failed);

            result.ExitCode.ShouldEqual(128);
            failed.ShouldBeFalse();
            downloads.ShouldEqual(1);
            git.CommandsRun.Count.ShouldEqual(2);
        }
    }
}
