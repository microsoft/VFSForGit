using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class UpdateIndexTests : GitRepoTests
    {
        public UpdateIndexTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        [Ignore("TODO 940287: git update-index --remove does not check if the file is on disk if the skip-worktree bit is set")]
        public void UpdateIndexRemoveFileOnDisk()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("update-index --remove Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void UpdateIndexRemoveFileOnDiskDontCheckStatus()
        {
            // TODO 940287: Remove this test and re-enable UpdateIndexRemoveFileOnDisk
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);

            // git-status will not match because update-index --remove does not check what is on disk if the skip-worktree bit is set,
            // meaning it will always remove the file from the index
            GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "update-index --remove Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "update-index --remove Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt");
            this.FilesShouldMatchCheckoutOfTargetBranch();

            // Add the files back to the index so the git-status that is run during teardown matches
            GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "update-index --add Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt");
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "update-index --add Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt");
        }

        protected override void CreateEnlistment()
        {
            base.CreateEnlistment();
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
        }
    }
}
