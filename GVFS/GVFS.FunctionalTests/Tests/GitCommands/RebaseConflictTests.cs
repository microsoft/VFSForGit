using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class RebaseConflictTests : GitRepoTests
    {
        public RebaseConflictTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        [Category(Categories.MacTODO.M3)]
        public void RebaseConflict()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        [Ignore("The file system is correct but getting 'refusing to lose untracked file at Test_ConflictTests\\ModifiedFiles\\ChangeInTargetDeleteInSource.txt'")]
        public void RebaseConflictWithFileReads()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ReadConflictTargetFiles();
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        [Category(Categories.MacTODO.M3)]
        public void RebaseConflict_ThenAbort()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rebase --abort");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        [Category(Categories.MacTODO.M3)]
        public void RebaseConflict_ThenSkip()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rebase --skip");
            this.FilesShouldMatchCheckoutOfSourceBranch();
        }

        [TestCase]
        [Category(Categories.MacTODO.M3)]
        public void RebaseConflict_RemoveDeletedTheirsFile()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rm Test_ConflictTests/ModifiedFiles/ChangeInSourceDeleteInTarget.txt");
        }

        [TestCase]
        [Category(Categories.MacTODO.M3)]
        public void RebaseConflict_AddThenContinue()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("add .");
            this.ValidateGitCommand("rebase --continue");
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void RebaseMultipleCommits()
        {
            string sourceCommit = "FunctionalTests/20170403_rebase_multiple_source";
            string targetCommit = "FunctionalTests/20170403_rebase_multiple_onto";

            this.ControlGitRepo.Fetch(sourceCommit);
            this.ControlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand("checkout " + sourceCommit);
            this.RunGitCommand("rebase origin/" + targetCommit);
            this.ValidateGitCommand("rebase --abort");
        }

        protected override void CreateEnlistment()
        {
            base.CreateEnlistment();
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
        }
    }
}
