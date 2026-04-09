using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class UpdateIndexTests : GitRepoTests
    {
        public UpdateIndexTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
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

        [TestCase]
        public void UpdateIndexRemoveAddFileOpenForWrite()
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

        [TestCase]
        public void UpdateIndexWithCacheInfo()
        {
            // Update Protocol.md with the contents from blob 583f1...
            string command = $"update-index --cacheinfo 100644 \"583f1a56db7cc884d54534c5d9c56b93a1e00a2b\n\" Protocol.md";

            this.ValidateGitCommand(command);
        }

        protected override void CreateEnlistment()
        {
            base.CreateEnlistment();
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
        }
    }
}
