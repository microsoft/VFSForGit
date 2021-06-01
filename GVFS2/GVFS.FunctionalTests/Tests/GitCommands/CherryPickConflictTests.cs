using GVFS.FunctionalTests.Properties;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class CherryPickConflictTests : GitRepoTests
    {
        public CherryPickConflictTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void CherryPickConflict()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("cherry-pick " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void CherryPickConflictWithFileReads()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ReadConflictTargetFiles();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("cherry-pick " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void CherryPickConflictWithFileReads2()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ReadConflictTargetFiles();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("cherry-pick " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
            this.ValidateGitCommand("cherry-pick --abort");
            this.FilesShouldMatchCheckoutOfTargetBranch();
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
        }

        [TestCase]
        public void CherryPickConflict_ThenAbort()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("cherry-pick " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("cherry-pick --abort");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void CherryPickConflict_ThenSkip()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("cherry-pick " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("cherry-pick --skip");
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void CherryPickConflict_UsingOurs()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("cherry-pick -Xours " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void CherryPickConflict_UsingTheirs()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("cherry-pick -Xtheirs " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchAfterConflict();
        }

        [TestCase]
        public void CherryPickNoCommit()
        {
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");
            this.ValidateGitCommand("cherry-pick --no-commit " + GitRepoTests.ConflictTargetBranch);
        }

        [TestCase]
        public void CherryPickNoCommitReset()
        {
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");
            this.ValidateGitCommand("cherry-pick --no-commit " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset");
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
