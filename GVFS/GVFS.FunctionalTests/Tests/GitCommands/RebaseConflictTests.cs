using GVFS.FunctionalTests.Properties;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class RebaseConflictTests : GitRepoTests
    {
        public RebaseConflictTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void RebaseConflict()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void RebaseConflictWithPrefetch()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.Enlistment.Prefetch("--files * --hydrate");
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void RebaseConflictWithFileReads()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ReadConflictTargetFiles();
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void RebaseConflict_ThenAbort()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rebase --abort");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void RebaseConflict_ThenSkip()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rebase --skip");
            this.FilesShouldMatchCheckoutOfSourceBranch();
        }

        [TestCase]
        public void RebaseConflict_RemoveDeletedTheirsFile()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.RunGitCommand("rebase " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("rm Test_ConflictTests/ModifiedFiles/ChangeInSourceDeleteInTarget.txt");
        }

        [TestCase]
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
            string sourceCommit = "FunctionalTests/20201014_rebase_multiple_source";
            string targetCommit = "FunctionalTests/20201014_rebase_multiple_onto";

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
