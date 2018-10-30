using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(DataSources), nameof(DataSources.AllBools))]
    [Category(Categories.GitCommands)]
    public class DeleteEmptyFolderTests : GitRepoTests
    {
        public DeleteEmptyFolderTests(bool validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void VerifyResetHardDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("reset --hard HEAD");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath);
        }

        [TestCase]
        public void VerifyCleanDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("clean -fd");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath);
        }

        private void SetupFolderDeleteTest()
        {
            ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20170202_RenameTestMergeTarget");
            this.DeleteFile("Test_EPF_GitCommandsTestOnlyFileFolder", "file.txt");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m\"Delete only file.\"");
        }
    }
}
