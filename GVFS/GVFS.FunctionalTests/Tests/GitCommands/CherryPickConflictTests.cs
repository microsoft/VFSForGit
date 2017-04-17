using GVFS.FunctionalTests.Category;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class CherryPickConflictTests : GitRepoTests
    {
        public CherryPickConflictTests() : base(enlistmentPerTest: true)
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
