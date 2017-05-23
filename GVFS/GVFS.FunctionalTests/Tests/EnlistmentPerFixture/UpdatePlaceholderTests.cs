using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class UpdatePlaceholderTests : TestsWithEnlistmentPerFixture
    {
        private const string TestParentFolderName = "Test_EPF_UpdatePlaceholderTests";
        private const string OldCommitId = "7bb7945e4767b43174c7468828b0eaf39bd2f110";
        private const string NewFilesAndChangesCommitId = "b4d932658def04a97da873fd6adab70014b8a523";
        private FileSystemRunner fileSystem;

        public UpdatePlaceholderTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [SetUp]
        public virtual void SetupForTest()
        {
            // Start each test at NewFilesAndChangesCommitId
            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);
            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
        }

        [TestCase, Order(1)]
        public void LockToPreventDelete_SingleFile()
        {
            string testFile1Contents = "TestContentsLockToPreventDelete \r\n";
            string testFile1Name = "test.txt";
            string testFile1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventDelete", testFile1Name));

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            using (SafeFileHandle testFile1Handle = this.CreateFile(testFile1Path, FileShare.Read))
            {
                testFile1Handle.IsInvalid.ShouldEqual(false);

                ProcessResult result = this.InvokeGitAgainstGVFSRepo("checkout " + OldCommitId);
                result.Errors.ShouldContain(
                    "GVFS was unable to delete the following files. To recover, close all handles to the files and run these commands:",
                    "git clean -f " + TestParentFolderName + "/LockToPreventDelete/" + testFile1Name);

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status -u",
                    "HEAD detached at " + OldCommitId,
                    "Untracked files:",
                    TestParentFolderName + "/LockToPreventDelete/" + testFile1Name);
            }

            this.GitCleanFile(TestParentFolderName + "/LockToPreventDelete/" + testFile1Name);
            this.GitStatusShouldBeClean(OldCommitId);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventDelete/" + testFile1Name);
            testFile1Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
        }

        [TestCase, Order(2)]
        public void LockToPreventDelete_MultipleFiles()
        {
            string testFile2Contents = "TestContentsLockToPreventDelete2 \r\n";
            string testFile3Contents = "TestContentsLockToPreventDelete3 \r\n";
            string testFile4Contents = "TestContentsLockToPreventDelete4 \r\n";

            string testFile2Name = "test2.txt";
            string testFile3Name = "test3.txt";
            string testFile4Name = "test4.txt";

            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventDelete", testFile2Name));
            string testFile3Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventDelete", testFile3Name));
            string testFile4Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventDelete", testFile4Name));

            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);

            using (SafeFileHandle testFile2Handle = this.CreateFile(testFile2Path, FileShare.Read))
            using (SafeFileHandle testFile3Handle = this.CreateFile(testFile3Path, FileShare.Read))
            using (SafeFileHandle testFile4Handle = this.CreateFile(testFile4Path, FileShare.Read))
            {
                testFile2Handle.IsInvalid.ShouldEqual(false);
                testFile3Handle.IsInvalid.ShouldEqual(false);
                testFile4Handle.IsInvalid.ShouldEqual(false);

                ProcessResult result = this.InvokeGitAgainstGVFSRepo("checkout " + OldCommitId);
                result.Errors.ShouldContain(
                    "GVFS was unable to delete the following files. To recover, close all handles to the files and run these commands:",
                    "git clean -f " + TestParentFolderName + "/LockToPreventDelete/" + testFile2Name,
                    "git clean -f " + TestParentFolderName + "/LockToPreventDelete/" + testFile3Name,
                    "git clean -f " + TestParentFolderName + "/LockToPreventDelete/" + testFile4Name);

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status -u",
                    "HEAD detached at " + OldCommitId,
                    "Untracked files:",
                    TestParentFolderName + "/LockToPreventDelete/" + testFile2Name,
                    TestParentFolderName + "/LockToPreventDelete/" + testFile3Name,
                    TestParentFolderName + "/LockToPreventDelete/" + testFile4Name);
            }

            this.GitCleanFile(TestParentFolderName + "/LockToPreventDelete/" + testFile2Name);
            this.GitCleanFile(TestParentFolderName + "/LockToPreventDelete/" + testFile3Name);
            this.GitCleanFile(TestParentFolderName + "/LockToPreventDelete/" + testFile4Name);

            this.GitStatusShouldBeClean(OldCommitId);

            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventDelete/" + testFile2Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventDelete/" + testFile3Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventDelete/" + testFile4Name);

            testFile2Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile3Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile4Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);
        }

        [TestCase, Order(3)]
        public void LockToPreventUpdate_SingleFile()
        {
            string testFile1Contents = "Commit2LockToPreventUpdate \r\n";
            string testFile1OldContents = "TestFileLockToPreventUpdate \r\n";
            string testFile1Name = "test.txt";
            string testFile1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdate", testFile1Name));

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            using (SafeFileHandle testFile1Handle = this.CreateFile(testFile1Path, FileShare.Read))
            {
                testFile1Handle.IsInvalid.ShouldEqual(false);

                ProcessResult result = this.InvokeGitAgainstGVFSRepo("checkout " + OldCommitId);
                result.Errors.ShouldContain(
                    "GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:",
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdate/" + testFile1Name);

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status",
                    "HEAD detached at " + OldCommitId,
                    "Changes not staged for commit:",
                    TestParentFolderName + "/LockToPreventUpdate/" + testFile1Name);
            }

            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdate/" + testFile1Name);
            this.GitStatusShouldBeClean(OldCommitId);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdate/" + testFile1Name);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1OldContents);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
        }

        [TestCase, Order(4)]
        public void LockToPreventUpdate_MultipleFiles()
        {
            string testFile2Contents = "Commit2LockToPreventUpdate2 \r\n";
            string testFile3Contents = "Commit2LockToPreventUpdate3 \r\n";
            string testFile4Contents = "Commit2LockToPreventUpdate4 \r\n";

            string testFile2OldContents = "TestFileLockToPreventUpdate2 \r\n";
            string testFile3OldContents = "TestFileLockToPreventUpdate3 \r\n";
            string testFile4OldContents = "TestFileLockToPreventUpdate4 \r\n";

            string testFile2Name = "test2.txt";
            string testFile3Name = "test3.txt";
            string testFile4Name = "test4.txt";

            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdate", testFile2Name));
            string testFile3Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdate", testFile3Name));
            string testFile4Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdate", testFile4Name));

            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);

            using (SafeFileHandle testFile2Handle = this.CreateFile(testFile2Path, FileShare.Read))
            using (SafeFileHandle testFile3Handle = this.CreateFile(testFile3Path, FileShare.Read))
            using (SafeFileHandle testFile4Handle = this.CreateFile(testFile4Path, FileShare.Read))
            {
                testFile2Handle.IsInvalid.ShouldEqual(false);
                testFile3Handle.IsInvalid.ShouldEqual(false);
                testFile4Handle.IsInvalid.ShouldEqual(false);

                ProcessResult result = this.InvokeGitAgainstGVFSRepo("checkout " + OldCommitId);
                result.Errors.ShouldContain(
                    "GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:",
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdate/" + testFile2Name,
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdate/" + testFile3Name,
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdate/" + testFile4Name);

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status",
                    "HEAD detached at " + OldCommitId,
                    "Changes not staged for commit:",
                    TestParentFolderName + "/LockToPreventUpdate/" + testFile2Name,
                    TestParentFolderName + "/LockToPreventUpdate/" + testFile3Name,
                    TestParentFolderName + "/LockToPreventUpdate/" + testFile4Name);
            }

            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdate/" + testFile2Name);
            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdate/" + testFile3Name);
            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdate/" + testFile4Name);

            this.GitStatusShouldBeClean(OldCommitId);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdate/" + testFile2Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdate/" + testFile3Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdate/" + testFile4Name);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2OldContents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3OldContents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4OldContents);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);
        }

        [TestCase, Order(5)]
        public void LockToPreventUpdateAndDelete()
        {
            string testFileUpdate1Contents = "Commit2LockToPreventUpdateAndDelete \r\n";
            string testFileUpdate2Contents = "Commit2LockToPreventUpdateAndDelete2 \r\n";
            string testFileUpdate3Contents = "Commit2LockToPreventUpdateAndDelete3 \r\n";
            string testFileDelete1Contents = "PreventDelete \r\n";
            string testFileDelete2Contents = "PreventDelete2 \r\n";
            string testFileDelete3Contents = "PreventDelete3 \r\n";

            string testFileUpdate1OldContents = "TestFileLockToPreventUpdateAndDelete \r\n";
            string testFileUpdate2OldContents = "TestFileLockToPreventUpdateAndDelete2 \r\n";
            string testFileUpdate3OldContents = "TestFileLockToPreventUpdateAndDelete3 \r\n";

            string testFileUpdate1Name = "test.txt";
            string testFileUpdate2Name = "test2.txt";
            string testFileUpdate3Name = "test3.txt";
            string testFileDelete1Name = "test_delete.txt";
            string testFileDelete2Name = "test_delete2.txt";
            string testFileDelete3Name = "test_delete3.txt";

            string testFileUpdate1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileUpdate1Name));
            string testFileUpdate2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileUpdate2Name));
            string testFileUpdate3Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileUpdate3Name));
            string testFileDelete1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileDelete1Name));
            string testFileDelete2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileDelete2Name));
            string testFileDelete3Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileDelete3Name));

            testFileUpdate1Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate1Contents);
            testFileUpdate2Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate2Contents);
            testFileUpdate3Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate3Contents);
            testFileDelete1Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete1Contents);
            testFileDelete2Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete2Contents);
            testFileDelete3Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete3Contents);

            using (SafeFileHandle testFileUpdate1Handle = this.CreateFile(testFileUpdate1Path, FileShare.Read))
            using (SafeFileHandle testFileUpdate2Handle = this.CreateFile(testFileUpdate2Path, FileShare.Read))
            using (SafeFileHandle testFileUpdate3Handle = this.CreateFile(testFileUpdate3Path, FileShare.Read))
            using (SafeFileHandle testFileDelete1Handle = this.CreateFile(testFileDelete1Path, FileShare.Read))
            using (SafeFileHandle testFileDelete2Handle = this.CreateFile(testFileDelete2Path, FileShare.Read))
            using (SafeFileHandle testFileDelete3Handle = this.CreateFile(testFileDelete3Path, FileShare.Read))
            {
                testFileUpdate1Handle.IsInvalid.ShouldEqual(false);
                testFileUpdate2Handle.IsInvalid.ShouldEqual(false);
                testFileUpdate3Handle.IsInvalid.ShouldEqual(false);
                testFileDelete1Handle.IsInvalid.ShouldEqual(false);
                testFileDelete2Handle.IsInvalid.ShouldEqual(false);
                testFileDelete3Handle.IsInvalid.ShouldEqual(false);

                ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + OldCommitId);
                checkoutResult.Errors.ShouldContain(
@"HEAD is now at " + OldCommitId + @"... Add test files for update UpdatePlaceholder tests

Waiting for GVFS to parse index and update placeholder files...Completed with errors.

GVFS was unable to delete the following files. To recover, close all handles to the files and run these commands:
    git clean -f Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete.txt
    git clean -f Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete2.txt
    git clean -f Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete3.txt

GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:
    git checkout -- Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test.txt
    git checkout -- Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test2.txt
    git checkout -- Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test3.txt");

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, 
                    "status",
                    "HEAD detached at " + OldCommitId,
                    "modified:   Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test.txt",
                    "modified:   Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test2.txt",
                    "modified:   Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test3.txt",
                    "Untracked files:\n  (use \"git add <file>...\" to include in what will be committed)\n\n\tTest_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete.txt\n\tTest_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete2.txt\n\tTest_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test_delete3.txt",
                    "no changes added to commit (use \"git add\" and/or \"git commit -a\")\n");
            }

            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate1Name);
            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate2Name);
            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate3Name);
            this.GitCleanFile(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete1Name);
            this.GitCleanFile(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete2Name);
            this.GitCleanFile(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete3Name);

            this.GitStatusShouldBeClean(OldCommitId);

            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate1Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate2Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate3Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete1Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete2Name);
            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete3Name);

            testFileUpdate1Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate1OldContents);
            testFileUpdate2Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate2OldContents);
            testFileUpdate3Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate3OldContents);
            testFileDelete1Path.ShouldNotExistOnDisk(this.fileSystem);
            testFileDelete2Path.ShouldNotExistOnDisk(this.fileSystem);
            testFileDelete3Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFileUpdate1Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate1Contents);
            testFileUpdate2Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate2Contents);
            testFileUpdate3Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate3Contents);
            testFileDelete1Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete1Contents);
            testFileDelete2Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete2Contents);
            testFileDelete3Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete3Contents);
        }

        [TestCase, Order(6)]
        public void LockWithFullShareUpdateAndDelete()
        {
            string testFileUpdate4Contents = "Commit2LockToPreventUpdateAndDelete4 \r\n";
            string testFileDelete4Contents = "PreventDelete4 \r\n";
            string testFileUpdate4OldContents = "TestFileLockToPreventUpdateAndDelete4 \r\n";

            string testFileUpdate4Name = "test4.txt";
            string testFileDelete4Name = "test_delete4.txt";

            string testFileUpdate4Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileUpdate4Name));
            string testFileDelete4Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdateAndDelete", testFileDelete4Name));

            testFileUpdate4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate4Contents);
            testFileDelete4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete4Contents);

            using (SafeFileHandle testFileUpdate4Handle = this.CreateFile(testFileUpdate4Path, FileShare.Read | FileShare.Delete))
            using (SafeFileHandle testFileDelete4Handle = this.CreateFile(testFileDelete4Path, FileShare.Read | FileShare.Delete))
            {
                testFileUpdate4Handle.IsInvalid.ShouldEqual(false);
                testFileDelete4Handle.IsInvalid.ShouldEqual(false);

                ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + OldCommitId);
                checkoutResult.Errors.ShouldContain(
@"HEAD is now at " + OldCommitId + @"... Add test files for update UpdatePlaceholder tests

Waiting for GVFS to parse index and update placeholder files...Completed with errors.

GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:
    git checkout -- Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test4.txt");

                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, 
                    "status",
                    "HEAD detached at " + OldCommitId,
                    "modified:   Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test4.txt",
                    "no changes added to commit (use \"git add\" and/or \"git commit -a\")\n");
            }

            this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate4Name);
            this.GitStatusShouldBeClean(OldCommitId);

            this.SparseCheckoutShouldContain(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate4Name);

            testFileUpdate4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate4OldContents);
            testFileDelete4Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFileUpdate4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate4Contents);
            testFileDelete4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete4Contents);
        }

        [TestCase, Order(7)]
        public void FileProjectedAfterPlaceholderDeleteFileAndCheckout()
        {
            string testFile1Contents = "ProjectAfterDeleteAndCheckout \r\n";
            string testFile2Contents = "ProjectAfterDeleteAndCheckout2 \r\n";
            string testFile3Contents = "ProjectAfterDeleteAndCheckout3 \r\n";

            string testFile1Name = "test.txt";
            string testFile2Name = "test2.txt";
            string testFile3Name = "test3.txt";

            string testFile1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "FileProjectedAfterPlaceholderDeleteFileAndCheckout", testFile1Name));
            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "FileProjectedAfterPlaceholderDeleteFileAndCheckout", testFile2Name));
            string testFile3Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "FileProjectedAfterPlaceholderDeleteFileAndCheckout", testFile3Name));

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);

            this.GitCheckoutCommitId(OldCommitId);
            this.GitStatusShouldBeClean(OldCommitId);

            testFile1Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile2Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile3Path.ShouldNotExistOnDisk(this.fileSystem);

            this.SparseCheckoutShouldNotContain(TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile1Name);
            this.SparseCheckoutShouldNotContain(TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile2Name);
            this.SparseCheckoutShouldNotContain(TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile3Name);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);
            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
        }

        [TestCase, Order(8)]
        public void LockMoreThanMaxReportedFileNames()
        {
            string updateFilesFolder = "FilesToUpdate";
            string deleteFilesFolder = "FilesToDelete";

            for (int i = 1; i <= 51; ++i)
            {
                this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", updateFilesFolder, i.ToString() + ".txt")).ShouldBeAFile(this.fileSystem);
                this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", deleteFilesFolder, i.ToString() + ".txt")).ShouldBeAFile(this.fileSystem);
            }

            List<SafeFileHandle> openHandles = new List<SafeFileHandle>();
            try
            {
                for (int i = 1; i <= 51; ++i)
                {
                    SafeFileHandle handle = this.CreateFile(
                        this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", updateFilesFolder, i.ToString() + ".txt")),
                        FileShare.Read);
                    openHandles.Add(handle);
                    handle.IsInvalid.ShouldEqual(false);

                    handle = this.CreateFile(
                        this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", deleteFilesFolder, i.ToString() + ".txt")),
                        FileShare.Read);
                    openHandles.Add(handle);
                    handle.IsInvalid.ShouldEqual(false);
                }

                ProcessResult result = this.InvokeGitAgainstGVFSRepo("checkout " + OldCommitId);
                result.Errors.ShouldContain(
                    "GVFS failed to update 102 files, run 'git status' to check the status of files in the repo");

                List<string> expectedOutputStrings = new List<string>()
                    {
                        "HEAD detached at " + OldCommitId,
                        "no changes added to commit (use \"git add\" and/or \"git commit -a\")\n"
                    };

                for (int expectedFilePrefix = 1; expectedFilePrefix <= 51; ++expectedFilePrefix)
                {
                    expectedOutputStrings.Add("modified:   Test_EPF_UpdatePlaceholderTests/MaxFileListCount/" + updateFilesFolder + "/" + expectedFilePrefix.ToString() + ".txt");
                    expectedOutputStrings.Add("Test_EPF_UpdatePlaceholderTests/MaxFileListCount/" + deleteFilesFolder + "/" + expectedFilePrefix.ToString() + ".txt");
                }

                GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "status -u", expectedOutputStrings.ToArray());
            }
            finally
            {
                foreach (SafeFileHandle handle in openHandles)
                {
                    handle.Dispose();
                }
            }

            for (int i = 1; i <= 51; ++i)
            {
                this.GitCheckoutToDiscardChanges(TestParentFolderName + "/MaxFileListCount/" + updateFilesFolder + "/" + i.ToString() + ".txt");
                this.GitCleanFile(TestParentFolderName + "/MaxFileListCount/" + deleteFilesFolder + "/" + i.ToString() + ".txt");
            }

            this.GitStatusShouldBeClean(OldCommitId);
            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);
            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
        }

        private ProcessResult InvokeGitAgainstGVFSRepo(string command)
        {
            return GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, command);
        }

        private void GitStatusShouldBeClean(string commitId)
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "HEAD detached at " + commitId,
                "nothing to commit, working tree clean");
        }

        private void GitCleanFile(string gitPath)
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "clean -f " + gitPath,
                    "Removing " + gitPath);
        }

        private void GitCheckoutToDiscardChanges(string gitPath)
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout -- " + gitPath);
        }

        private void GitCheckoutCommitId(string commitId)
        {
            this.InvokeGitAgainstGVFSRepo("checkout " + commitId).Errors.ShouldContain("HEAD is now at " + commitId);
        }

        private void SparseCheckoutShouldContain(string gitPath)
        {
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(gitPath);
        }

        private void SparseCheckoutShouldNotContain(string gitPath)
        {
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: gitPath);
        }

        private SafeFileHandle CreateFile(string path, FileShare shareMode)
        {
            return NativeMethods.CreateFile(
                path,
                (uint)FileAccess.Read,
                shareMode,
                IntPtr.Zero,
                FileMode.Open,
                (uint)FileAttributes.Normal,
                IntPtr.Zero);
        }
    }
}
