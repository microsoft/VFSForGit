using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

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

        [TestCase]
        public void CheckoutCommitWhereFileContentsChangeAfterRead()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);

            string fileName = "SameChange.txt";

            // In commit 170b13ce1990c53944403a70e93c257061598ae0 the initial files for the FunctionalTests/20170206_Conflict_Source branch were created
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");
            this.FileContentsShouldMatch(@"Test_ConflictTests\ModifiedFiles\" + fileName);

            // A read should not add the file to the sparse-checkout
            string sparseFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: fileName);

            this.ValidateGitCommand("checkout FunctionalTests/20170206_Conflict_Source");
            this.FileContentsShouldMatch(@"Test_ConflictTests\ModifiedFiles\" + fileName);
            sparseFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: fileName);
        }

        [TestCase]
        public void CheckoutCommitWhereFileDeletedAfterRead()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);

            string fileName = "DeleteInSource.txt";
            string filePath = @"Test_ConflictTests\DeletedFiles\" + fileName;

            // In commit 170b13ce1990c53944403a70e93c257061598ae0 the initial files for the FunctionalTests/20170206_Conflict_Source branch were created
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");
            this.FileContentsShouldMatch(filePath);

            // A read should not add the file to the sparse-checkout
            string sparseFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: fileName);

            this.ValidateGitCommand("checkout FunctionalTests/20170206_Conflict_Source");
            this.ShouldNotExistOnDisk(filePath);
            sparseFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: fileName);
        }

        [TestCase]
        public void CheckoutBranchAfterReadingFileAndVerifyContentsCorrect()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.FilesShouldMatchCheckoutOfTargetBranch();

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.FilesShouldMatchCheckoutOfSourceBranch();

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldEqual("/.gitattributes\n");
        }

        [TestCase]
        public void CheckoutBranchAfterReadingAllFilesAndVerifyContentsCorrect()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, skipEmptyDirectories: true, compareContent: true);

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, skipEmptyDirectories: true, compareContent: true);

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldEqual("/.gitattributes\n");
        }

        [TestCase]
        public void DeleteEmptyFolderPlaceholderAndCheckoutBranchThatHasFolder()
        {
            // this.ControlGitRepo.Commitish should not have the folder Test_ConflictTests\AddedFiles
            string testFolder = @"Test_ConflictTests\AddedFiles";
            this.ShouldNotExistOnDisk(testFolder);

            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            string testFile = testFolder + @"\AddedByBothDifferentContent.txt";
            this.FileContentsShouldMatch(testFile);

            // Move back to this.ControlGitRepo.Commitish where testFolder and testFile are not in the repo
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ShouldNotExistOnDisk(testFile);

            // Test_ConflictTests\AddedFiles will only be on disk in the GVFS enlistment, delete it there
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, testFolder);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, testFolder);
            controlFolder.ShouldNotExistOnDisk(this.FileSystem);
            this.FileSystem.DeleteDirectory(virtualFolder);
            virtualFolder.ShouldNotExistOnDisk(this.FileSystem);

            // Move back to GitRepoTests.ConflictSourceBranch where testFolder and testFile are present
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.FileContentsShouldMatch(testFile);
        }

        [TestCase]
        public void DeleteEmptyFolderPlaceholderAndCheckoutBranchThatDoesNotHaveFolder()
        {
            // this.ControlGitRepo.Commitish should not have the folder Test_ConflictTests\AddedFiles
            string testFolder = @"Test_ConflictTests\AddedFiles";
            this.ShouldNotExistOnDisk(testFolder);

            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);

            string testFile = testFolder + @"\AddedByBothDifferentContent.txt";
            this.FileContentsShouldMatch(testFile);
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ShouldNotExistOnDisk(testFile);

            this.ValidateGitCommand("checkout -b tests/functional/DeleteEmptyFolderPlaceholderAndCheckoutBranchThatDoesNotHaveFolder" + this.ControlGitRepo.Commitish);

            // Test_ConflictTests\AddedFiles will only be on disk in the GVFS enlistment, delete it there
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, testFolder);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, testFolder);
            controlFolder.ShouldNotExistOnDisk(this.FileSystem);
            this.FileSystem.DeleteDirectory(virtualFolder);
            virtualFolder.ShouldNotExistOnDisk(this.FileSystem);

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
        }

        [TestCase]
        public void EditFileReadFileAndCheckoutConflict()
        {
            // editFilePath was changed on ConflictTargetBranch
            string editFilePath = @"Test_ConflictTests\ModifiedFiles\ChangeInTarget.txt";

            // readFilePath has different contents on ConflictSourceBranch and ConflictTargetBranch
            string readFilePath = @"Test_ConflictTests\ModifiedFiles\ChangeInSource.txt";

            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);

            this.EditFile(editFilePath, "New content");
            this.FileContentsShouldMatch(readFilePath);
            string originalReadFileContents = this.Enlistment.GetVirtualPathTo(readFilePath).ShouldBeAFile(this.FileSystem).WithContents();

            // This checkout will hit a conflict due to the changes in editFilePath
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.FileContentsShouldMatch(readFilePath);
            this.FileContentsShouldMatch(editFilePath);

            // The contents of originalReadFileContents should not have changed
            this.Enlistment.GetVirtualPathTo(readFilePath).ShouldBeAFile(this.FileSystem).WithContents(originalReadFileContents);

            this.ValidateGitCommand("checkout -- " + editFilePath.Replace('\\', '/'));
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.FileContentsShouldMatch(readFilePath);
            this.FileContentsShouldMatch(editFilePath);
            this.Enlistment.GetVirtualPathTo(readFilePath).ShouldBeAFile(this.FileSystem).WithContents().ShouldNotEqual(originalReadFileContents);

            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.FileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: Path.GetFileName(readFilePath));
        }

        [TestCase]
        public void MarkFileAsReadOnlyAndCheckoutCommitWhereFileIsDifferent()
        {
            string filePath = @"Test_ConflictTests\ModifiedFiles\ConflictingChange.txt";

            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);

            this.SetFileAsReadOnly(filePath);

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.FileContentsShouldMatch(filePath);
        }

        [TestCase]
        public void MarkFileAsReadOnlyAndCheckoutCommitWhereFileIsDeleted()
        {
            string filePath = @"Test_ConflictTests\AddedFiles\AddedBySource.txt";

            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);

            this.SetFileAsReadOnly(filePath);

            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ShouldNotExistOnDisk(filePath);
        }

        [TestCase]
        public void ModifyAndCheckoutFirstOfSeveralFilesWhoseNamesAppearBeforeDot()
        {
            // Commit 14cf226119766146b1fa5c5aa4cd0896d05f6b63 has the files (a).txt and (z).txt 
            // in the DeleteFileWithNameAheadOfDotAndSwitchCommits folder
            string originalContent = "Test contents for (a).txt";
            string newContent = "content to append";

            this.ValidateGitCommand("checkout 14cf226119766146b1fa5c5aa4cd0896d05f6b63");
            this.EditFile("DeleteFileWithNameAheadOfDotAndSwitchCommits\\(a).txt", newContent);
            this.FileShouldHaveContents("DeleteFileWithNameAheadOfDotAndSwitchCommits\\(a).txt", originalContent + newContent);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("checkout -- DeleteFileWithNameAheadOfDotAndSwitchCommits/(a).txt");
            this.ValidateGitCommand("status");
            this.FileShouldHaveContents("DeleteFileWithNameAheadOfDotAndSwitchCommits\\(a).txt", originalContent);
        }

        [TestCase]
        public void ResetMixedToCommitWithNewFileThenCheckoutNewBranchAndCheckoutCommitWithNewFile()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);

            // Commit 170b13ce1990c53944403a70e93c257061598ae0 was prior to the additional of these 
            // three files in commit f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1:
            //    Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt
            //    Test_ConflictTests/AddedFiles/AddedByBothSameContent.txt
            //    Test_ConflictTests/AddedFiles/AddedBySource.txt            
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");
            this.ValidateGitCommand("reset --mixed f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1");

            // Use RunGitCommand rather than ValidateGitCommand as G4W optimizations for "checkout -b" mean that the
            // command will not report modified and deleted files
            this.RunGitCommand("checkout -b tests/functional/ResetMixedToCommitWithNewFileThenCheckoutNewBranchAndCheckoutCommitWithNewFile");
            this.ValidateGitCommand("checkout f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1");
        }
    }
}
