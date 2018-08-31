using GVFS.FunctionalTests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    [Category(Categories.MacTODO.M3)]
    public class ResetHardTests : GitRepoTests
    {
        private const string ResetHardCommand = "reset --hard";

        public ResetHardTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        public void VerifyResetHardDeletesEmptyFolders()
        {
            ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20170202_RenameTestMergeTarget");
            this.ValidateGitCommand("reset --hard HEAD~1");
            this.ShouldNotExistOnDisk("Test_EPF_GitCommandsTestOnlyFileFolder");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath);
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
