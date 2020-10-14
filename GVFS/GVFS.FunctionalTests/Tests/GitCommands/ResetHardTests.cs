using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class ResetHardTests : GitRepoTests
    {
        private const string ResetHardCommand = "reset --hard";

        public ResetHardTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        [Ignore("This doesn't work right now. Tracking if this is a ProjFS problem. See #1696 for tracking.")]
        public void VerifyResetHardDeletesEmptyFolders()
        {
            this.ControlGitRepo.Fetch("FunctionalTests/20201014_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20201014_RenameTestMergeTarget");
            this.ValidateGitCommand("reset --hard HEAD~1");
            this.ShouldNotExistOnDisk("Test_EPF_GitCommandsTestOnlyFileFolder");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);
        }

        [TestCase]
        public void ResetHardWithDirectoryNameSameAsFile()
        {
            this.SetupForFileDirectoryTest();
            this.ValidateFileDirectoryTest(ResetHardCommand);
        }

        [TestCase]
        public void ResetHardWithDirectoryNameSameAsFileEnumerate()
        {
            this.RunFileDirectoryEnumerateTest(ResetHardCommand);
        }

        [TestCase]
        public void ResetHardWithDirectoryNameSameAsFileWithRead()
        {
            this.RunFileDirectoryReadTest(ResetHardCommand);
        }

        [TestCase]
        public void ResetHardWithDirectoryNameSameAsFileWithWrite()
        {
            this.RunFileDirectoryWriteTest(ResetHardCommand);
        }

        [TestCase]
        public void ResetHardDirectoryWithOneFile()
        {
            this.SetupForFileDirectoryTest(commandBranch: GitRepoTests.DirectoryWithFileAfterBranch);
            this.ValidateFileDirectoryTest(ResetHardCommand, commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void ResetHardDirectoryWithOneFileEnumerate()
        {
            this.RunFileDirectoryEnumerateTest(ResetHardCommand, commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void ResetHardDirectoryWithOneFileRead()
        {
            this.RunFileDirectoryReadTest(ResetHardCommand, commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void ResetHardDirectoryWithOneFileWrite()
        {
            this.RunFileDirectoryWriteTest(ResetHardCommand, commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }
    }
}
