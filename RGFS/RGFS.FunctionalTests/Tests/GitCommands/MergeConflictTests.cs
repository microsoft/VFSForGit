using RGFS.FunctionalTests.Category;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;

namespace RGFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
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
            // This is compared against the message emitted by RGFS.Hooks\Program.cs
            string expectedErrorMessagePart = "--no-renames --no-breaks";

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("merge " + GitRepoTests.ConflictSourceBranch, checkStatus: false);

            ProcessResult result1 = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status");
            result1.Errors.Contains(expectedErrorMessagePart);

            ProcessResult result2 = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status --no-renames");
            result2.Errors.Contains(expectedErrorMessagePart);

            ProcessResult result3 = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status --no-breaks");
            result3.Errors.Contains(expectedErrorMessagePart);

            // only renames in config
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local status.renames false");
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status --no-breaks").Errors.ShouldBeEmpty();
            ProcessResult result4 = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status");
            result4.Errors.Contains(expectedErrorMessagePart);

            // only breaks in config
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local --unset status.renames");
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local status.breaks false");
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status --no-renames").Errors.ShouldBeEmpty();
            ProcessResult result5 = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status");
            result5.Errors.Contains(expectedErrorMessagePart);

            // Complete setup to ensure teardown succeeds
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local status.renames false");
            GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local status.breaks false");
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
