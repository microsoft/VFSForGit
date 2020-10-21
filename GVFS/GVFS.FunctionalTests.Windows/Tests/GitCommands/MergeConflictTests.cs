using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class MergeConflictTests : GitRepoTests
    {
        public MergeConflictTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void MergeConflict()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void MergeConflictWithFileReads()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ReadConflictTargetFiles();
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void MergeConflict_ThenAbort()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("merge --abort");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void MergeConflict_UsingOurs()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand($"merge -s ours {GitRepoTests.ConflictSourceBranch}");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void MergeConflict_UsingStrategyTheirs()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand($"merge -s recursive -Xtheirs {GitRepoTests.ConflictSourceBranch}");
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void MergeConflict_UsingStrategyOurs()
        {
            // No need to tear down this config since these tests are for enlistment per test.
            this.SetupRenameDetectionAvoidanceInConfig();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand($"merge -s recursive -Xours {GitRepoTests.ConflictSourceBranch}");
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void MergeConflictEnsureStatusFailsDueToConfig()
        {
            // This is compared against the message emitted by GVFS.Hooks\Program.cs
            string expectedErrorMessagePart = "--no-renames";

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch, checkStatus: false);

            ProcessResult result1 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status");
            result1.Errors.Contains(expectedErrorMessagePart);

            ProcessResult result2 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status --no-renames");
            result2.Errors.Contains(expectedErrorMessagePart);

            // Complete setup to ensure teardown succeeds
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local test.renames false");
        }

        protected override void CreateEnlistment()
        {
            base.CreateEnlistment();
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
        }

        private void SetupRenameDetectionAvoidanceInConfig()
        {
            // Tell the pre-command hook that it shouldn't check for "--no-renames" when runing "git status"
            // as the control repo won't do that.  When the pre-command hook has been updated to properly
            // check for "status.renames" we can set that value here instead.
            this.ValidateGitCommand("config --local test.renames false");
        }
    }
}
