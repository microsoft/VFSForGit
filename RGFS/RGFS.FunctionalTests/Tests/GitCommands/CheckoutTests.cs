using RGFS.FunctionalTests.Category;
using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RGFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class CheckoutTests : GitRepoTests
    {
        public CheckoutTests() : base(enlistmentPerTest: true)
        {
        }

        private enum NativeFileAttributes : uint
        {
            FILE_ATTRIBUTE_READONLY = 1,
            FILE_ATTRIBUTE_HIDDEN = 2,
            FILE_ATTRIBUTE_SYSTEM = 4,
            FILE_ATTRIBUTE_DIRECTORY = 16,
            FILE_ATTRIBUTE_ARCHIVE = 32,
            FILE_ATTRIBUTE_DEVICE = 64,
            FILE_ATTRIBUTE_NORMAL = 128,
            FILE_ATTRIBUTE_TEMPORARY = 256,
            FILE_ATTRIBUTE_SPARSEFILE = 512,
            FILE_ATTRIBUTE_REPARSEPOINT = 1024,
            FILE_ATTRIBUTE_COMPRESSED = 2048,
            FILE_ATTRIBUTE_OFFLINE = 4096,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 8192,
            FILE_ATTRIBUTE_ENCRYPTED = 16384,
            FILE_FLAG_FIRST_PIPE_INSTANCE = 524288,
            FILE_FLAG_OPEN_NO_RECALL = 1048576,
            FILE_FLAG_OPEN_REPARSE_POINT = 2097152,
            FILE_FLAG_POSIX_SEMANTICS = 16777216,
            FILE_FLAG_BACKUP_SEMANTICS = 33554432,
            FILE_FLAG_DELETE_ON_CLOSE = 67108864,
            FILE_FLAG_SEQUENTIAL_SCAN = 134217728,
            FILE_FLAG_RANDOM_ACCESS = 268435456,
            FILE_FLAG_NO_BUFFERING = 536870912,
            FILE_FLAG_OVERLAPPED = 1073741824,
            FILE_FLAG_WRITE_THROUGH = 2147483648
        }

        private enum NativeFileAccess : uint
        {
            FILE_READ_DATA = 1,
            FILE_LIST_DIRECTORY = 1,
            FILE_WRITE_DATA = 2,
            FILE_ADD_FILE = 2,
            FILE_APPEND_DATA = 4,
            FILE_ADD_SUBDIRECTORY = 4,
            FILE_CREATE_PIPE_INSTANCE = 4,
            FILE_READ_EA = 8,
            FILE_WRITE_EA = 16,
            FILE_EXECUTE = 32,
            FILE_TRAVERSE = 32,
            FILE_DELETE_CHILD = 64,
            FILE_READ_ATTRIBUTES = 128,
            FILE_WRITE_ATTRIBUTES = 256,
            SPECIFIC_RIGHTS_ALL = 65535,
            DELETE = 65536,
            READ_CONTROL = 131072,
            STANDARD_RIGHTS_READ = 131072,
            STANDARD_RIGHTS_WRITE = 131072,
            STANDARD_RIGHTS_EXECUTE = 131072,
            WRITE_DAC = 262144,
            WRITE_OWNER = 524288,
            STANDARD_RIGHTS_REQUIRED = 983040,
            SYNCHRONIZE = 1048576,
            FILE_GENERIC_READ = 1179785,
            FILE_GENERIC_EXECUTE = 1179808,
            FILE_GENERIC_WRITE = 1179926,
            STANDARD_RIGHTS_ALL = 2031616,
            FILE_ALL_ACCESS = 2032127,
            ACCESS_SYSTEM_SECURITY = 16777216,
            MAXIMUM_ALLOWED = 33554432,
            GENERIC_ALL = 268435456,
            GENERIC_EXECUTE = 536870912,
            GENERIC_WRITE = 1073741824,
            GENERIC_READ = 2147483648
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

            // Test_ConflictTests\AddedFiles will only be on disk in the RGFS enlistment, delete it there
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

            // Test_ConflictTests\AddedFiles will only be on disk in the RGFS enlistment, delete it there
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

        // ReadFileAfterTryingToReadFileAtCommitWhereFileDoesNotExist is meant to exercise the NegativePathCache and its
        // behavior when projections change
        [TestCase]
        public void ReadFileAfterTryingToReadFileAtCommitWhereFileDoesNotExist()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);

            // Commit 170b13ce1990c53944403a70e93c257061598ae0 was prior to the additional of these
            // three files in commit f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1:
            //    Test_ConflictTests/AddedFiles/AddedByBothDifferentContent.txt
            //    Test_ConflictTests/AddedFiles/AddedByBothSameContent.txt
            //    Test_ConflictTests/AddedFiles/AddedBySource.txt            
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");

            // Files should not exist
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothDifferentContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothSameContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedBySource.txt");

            // Check a second time to exercise the GvFlt negative cache
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothDifferentContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothSameContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedBySource.txt");

            // Switch to commit where files should exist
            this.ValidateGitCommand("checkout f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1");

            // Confirm files exist
            this.FileContentsShouldMatch(@"Test_ConflictTests\AddedFiles\AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch(@"Test_ConflictTests\AddedFiles\AddedByBothSameContent.txt");
            this.FileContentsShouldMatch(@"Test_ConflictTests\AddedFiles\AddedBySource.txt");

            // Switch to commit where files should not exist
            this.ValidateGitCommand("checkout 170b13ce1990c53944403a70e93c257061598ae0");

            // Verify files do not not exist
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothDifferentContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothSameContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedBySource.txt");

            // Check a second time to exercise the GvFlt negative cache
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothDifferentContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedByBothSameContent.txt");
            this.ShouldNotExistOnDisk(@"Test_ConflictTests\AddedFiles\AddedBySource.txt");
        }

        [TestCase]
        public void CheckoutBranchWithOpenHandleBlockingRepoMetdataUpdate()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            ManualResetEventSlim testReady = new ManualResetEventSlim(initialState: false);
            ManualResetEventSlim fileLocked = new ManualResetEventSlim(initialState: false);
            Task task = Task.Run(() =>
            {
                int attempts = 0;
                while (attempts < 100)
                {
                    try
                    {
                        using (FileStream stream = new FileStream(Path.Combine(this.Enlistment.DotRGFSRoot, "databases", "RepoMetadata.dat"), FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            fileLocked.Set();
                            testReady.Set();
                            Thread.Sleep(15000);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        ++attempts;
                        Thread.Sleep(50);
                    }
                }

                testReady.Set();
            });

            // Wait for task to acquire the handle
            testReady.Wait();
            fileLocked.IsSet.ShouldBeTrue("Failed to obtain exclusive file handle.  Exclusive handle required to validate behavior");

            try
            {                
                this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            }
            catch (Exception)
            {
                // If the test fails, we should wait for the Task to complete so that it does not keep a handle open
                task.Wait();
                throw;
            }
        }

        [TestCase]
        public void CheckoutBranchWithOpenHandleBlockingProjectionDeleteAndRepoMetdataUpdate()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            ManualResetEventSlim testReady = new ManualResetEventSlim(initialState: false);
            ManualResetEventSlim fileLocked = new ManualResetEventSlim(initialState: false);
            Task task = Task.Run(() =>
            {
                int attempts = 0;
                while (attempts < 100)
                {
                    try
                    {
                        using (FileStream projectionStream = new FileStream(Path.Combine(this.Enlistment.DotRGFSRoot, "RGFS_projection"), FileMode.Open, FileAccess.Read, FileShare.None))
                        using (FileStream metadataStream = new FileStream(Path.Combine(this.Enlistment.DotRGFSRoot, "databases", "RepoMetadata.dat"), FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            fileLocked.Set();
                            testReady.Set();
                            Thread.Sleep(15000);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        ++attempts;
                        Thread.Sleep(50);
                    }
                }

                testReady.Set();
            });

            // Wait for task to acquire the handle
            testReady.Wait();
            fileLocked.IsSet.ShouldBeTrue("Failed to obtain exclusive file handle.  Exclusive handle required to validate behavior");

            try
            {                
                this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            }
            catch (Exception)
            {
                // If the test fails, we should wait for the Task to complete so that it does not keep a handle open
                task.Wait();
                throw;
            }
        }

        [TestCase]
        public void CheckoutBranchWithStaleRepoMetadataTmpFileOnDisk()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            this.FileSystem.WriteAllText(Path.Combine(this.Enlistment.DotRGFSRoot, "databases", "RepoMetadata.dat.tmp"), "Stale RepoMetadata.dat.tmp file");
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
        }

        [TestCase]
        public void CheckoutBranchWhileOutsideToolDoesNotAllowDeleteOfOpenRepoMetadata()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            ManualResetEventSlim testReady = new ManualResetEventSlim(initialState: false);
            ManualResetEventSlim fileLocked = new ManualResetEventSlim(initialState: false);
            Task task = Task.Run(() =>
            {
                int attempts = 0;
                while (attempts < 100)
                {
                    try
                    {
                        using (FileStream stream = new FileStream(Path.Combine(this.Enlistment.DotRGFSRoot, "databases", "RepoMetadata.dat"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fileLocked.Set();
                            testReady.Set();
                            Thread.Sleep(15000);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        ++attempts;
                        Thread.Sleep(50);
                    }
                }

                testReady.Set();
            });

            // Wait for task to acquire the handle
            testReady.Wait();
            fileLocked.IsSet.ShouldBeTrue("Failed to obtain file handle.  Handle required to validate behavior");

            try
            {                
                this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            }
            catch (Exception)
            {
                // If the test fails, we should wait for the Task to complete so that it does not keep a handle open
                task.Wait();
                throw;
            }
        }

        [TestCase]
        public void CheckoutBranchWhileOutsideToolHasExclusiveReadHandleOnDatabasesFolder()
        {
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);

            ManualResetEventSlim testReady = new ManualResetEventSlim(initialState: false);
            ManualResetEventSlim folderLocked = new ManualResetEventSlim(initialState: false);
            Task task = Task.Run(() =>
            {
                int attempts = 0;
                string databasesPath = Path.Combine(this.Enlistment.DotRGFSRoot, "databases");
                while (attempts < 100)
                {
                    using (SafeFileHandle result = CreateFile(
                        databasesPath,
                        NativeFileAccess.GENERIC_READ,
                        FileShare.Read,
                        IntPtr.Zero,
                        FileMode.Open,
                        NativeFileAttributes.FILE_FLAG_BACKUP_SEMANTICS | NativeFileAttributes.FILE_FLAG_OPEN_REPARSE_POINT,
                        IntPtr.Zero))
                    {
                        if (result.IsInvalid)
                        {
                            ++attempts;
                            Thread.Sleep(50);
                        }
                        else
                        {
                            folderLocked.Set();
                            testReady.Set();
                            Thread.Sleep(15000);
                            return;
                        }
                    }
                }

                testReady.Set();
            });

            // Wait for task to acquire the handle
            testReady.Wait();
            folderLocked.IsSet.ShouldBeTrue("Failed to obtain exclusive file handle.  Handle required to validate behavior");

            try
            {                
                this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            }
            catch (Exception)
            {
                // If the test fails, we should wait for the Task to complete so that it does not keep a handle open
                task.Wait();
                throw;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            [In] string fileName,
            [MarshalAs(UnmanagedType.U4)] NativeFileAccess desiredAccess,
            FileShare shareMode,
            [In] IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)]FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)]NativeFileAttributes flagsAndAttributes,
            [In] IntPtr templateFile);
    }
}
