using GVFS.FunctionalTests.Category;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class CheckoutTests : GitRepoTests
    {
        public CheckoutTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        public void CheckoutNewBranchFromStartingPointTest()
        {
            // In commit 575d597cf09b2cd1c0ddb4db21ce96979010bbcb the CheckoutNewBranchFromStartingPointTest files were not present
            this.ValidateGitCommand("checkout 575d597cf09b2cd1c0ddb4db21ce96979010bbcb");
            this.ShouldNotExistOnDisk("GitCommandsTests\\CheckoutNewBranchFromStartingPointTest\\test1.txt");
            this.ShouldNotExistOnDisk("GitCommandsTests\\CheckoutNewBranchFromStartingPointTest\\test2.txt");

            // In commit 27cc59d3e9a996f1fdc1230c8a80553b316a1d00 the CheckoutNewBranchFromStartingPointTest files were present
            this.ValidateGitCommand("checkout -b tests/functional/CheckoutNewBranchFromStartingPointTest 27cc59d3e9a996f1fdc1230c8a80553b316a1d00");
            this.FileShouldHaveContents("GitCommandsTests\\CheckoutNewBranchFromStartingPointTest\\test1.txt", "TestFile1 \r\n");
            this.FileShouldHaveContents("GitCommandsTests\\CheckoutNewBranchFromStartingPointTest\\test2.txt", "TestFile2 \r\n");

            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void CheckoutOrhpanBranchFromStartingPointTest()
        {
            // In commit 27cc59d3e9a996f1fdc1230c8a80553b316a1d00 the CheckoutOrhpanBranchFromStartingPointTest files were not present
            this.ValidateGitCommand("checkout 575d597cf09b2cd1c0ddb4db21ce96979010bbcb");
            this.ShouldNotExistOnDisk("GitCommandsTests\\CheckoutOrhpanBranchFromStartingPointTest\\test1.txt");
            this.ShouldNotExistOnDisk("GitCommandsTests\\CheckoutOrhpanBranchFromStartingPointTest\\test2.txt");

            // In commit eff45342f895742b7d0a812f49611334e0b5b785 the CheckoutOrhpanBranchFromStartingPointTest files were present
            this.ValidateGitCommand("checkout --orphan tests/functional/CheckoutOrhpanBranchFromStartingPointTest eff45342f895742b7d0a812f49611334e0b5b785");
            this.FileShouldHaveContents("GitCommandsTests\\CheckoutOrhpanBranchFromStartingPointTest\\test1.txt", "TestFile1 \r\n");
            this.FileShouldHaveContents("GitCommandsTests\\CheckoutOrhpanBranchFromStartingPointTest\\test2.txt", "TestFile2 \r\n");

            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void MoveFileFromDotGitFolderToWorkingDirectoryAndAddAndCheckout()
        {
            string testFileContents = "Test file contents for MoveFileFromDotGitFolderToWorkingDirectoryAndAddAndCheckout";
            string filename = "AddedBySource.txt";
            string dotGitFilePath = @".git\" + filename;
            string targetPath = @"Test_ConflictTests\AddedFiles\" + filename;

            // In commit 27cc59d3e9a996f1fdc1230c8a80553b316a1d00 Test_ConflictTests\AddedFiles\AddedBySource.txt does not exist
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");

            string newBranchName = "tests/functional/MoveFileFromDotGitFolderToWorkingDirectoryAndAddAndCheckout";
            this.ValidateGitCommand("checkout -b " + newBranchName);

            this.ShouldNotExistOnDisk(targetPath);
            this.CreateFile(dotGitFilePath, testFileContents);
            this.FileShouldHaveContents(dotGitFilePath, testFileContents);

            // Move file to working directory
            this.MoveFile(dotGitFilePath, targetPath);
            this.FileContentsShouldMatch(targetPath);

            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for MoveFileFromDotGitFolderToWorkingDirectoryAndAddAndCheckout\"");

            // In commit f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1 Test_ConflictTests\AddedFiles\AddedBySource.txt was added
            this.ValidateGitCommand("checkout f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1");
            this.FileContentsShouldMatch(targetPath);
        }

        [TestCase]
        public void CheckoutBranchNoCrashOnStatus()
        {
            this.ControlGitRepo.Fetch("FunctionalTests/20170331_git_crash");
            this.ValidateGitCommand("checkout FunctionalTests/20170331_git_crash");
            this.ValidateGitCommand("status");
        }
    }
}
