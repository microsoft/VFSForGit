using GVFS.FunctionalTests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class DeleteEmptyFolderTests : GitRepoTests
    {
        public DeleteEmptyFolderTests(int validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void VerifyResetHardDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("reset --hard HEAD");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);
        }

        [TestCase]
        public void VerifyCleanDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("clean -fd");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);
        }

        private void SetupFolderDeleteTest()
        {
            this.ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20170202_RenameTestMergeTarget");
            this.DeleteFile("Test_EPF_GitCommandsTestOnlyFileFolder", "file.txt");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m\"Delete only file.\"");
        }
    }
}
