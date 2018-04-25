using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class MergeConflictTests : GitRepoTests
    {
        public MergeConflictTests() : base(enlistmentPerTest: true)
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
            string expectedErrorMessagePart = "--no-renames --no-breaks";

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch, checkStatus: false);

            ProcessResult result1 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status");
            result1.Errors.Contains(expectedErrorMessagePart);

            ProcessResult result2 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status --no-renames");
            result2.Errors.Contains(expectedErrorMessagePart);

            ProcessResult result3 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status --no-breaks");
            result3.Errors.Contains(expectedErrorMessagePart);

            // only renames in config
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local status.renames false");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status --no-breaks").Errors.ShouldBeEmpty();
            ProcessResult result4 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status");
            result4.Errors.Contains(expectedErrorMessagePart);

            // only breaks in config
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local --unset status.renames");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local status.breaks false");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status --no-renames").Errors.ShouldBeEmpty();
            ProcessResult result5 = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status");
            result5.Errors.Contains(expectedErrorMessagePart);

            // Complete setup to ensure teardown succeeds
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local status.renames false");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local status.breaks false");
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
            this.ValidateGitCommand("config --local status.renames false");
            this.ValidateGitCommand("config --local status.breaks false");
        }
    }
}
