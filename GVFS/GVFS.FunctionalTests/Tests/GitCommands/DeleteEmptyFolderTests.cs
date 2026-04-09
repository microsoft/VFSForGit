using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class DeleteEmptyFolderTests : GitRepoTests
    {
        public DeleteEmptyFolderTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        [Ignore("This doesn't work right now. Tracking if this is a ProjFS problem. See #1696 for tracking.")]
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
            this.ControlGitRepo.Fetch("FunctionalTests/20201014_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20201014_RenameTestMergeTarget");
            this.DeleteFile("Test_EPF_GitCommandsTestOnlyFileFolder", "file.txt");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m\"Delete only file.\"");
        }
    }
}
