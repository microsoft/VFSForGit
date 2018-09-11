using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class UpdatePlaceholderTests : TestsWithEnlistmentPerFixture
    {
        private const string TestParentFolderName = "Test_EPF_UpdatePlaceholderTests";
        private const string OldCommitId = "5d7a7d4db1734fb468a4094469ec58d26301b59d";
        private const string NewFilesAndChangesCommitId = "fec239ea12de1eda6ae5329d4f345784d5b61ff9";
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

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(1)]
        [Category(Categories.WindowsOnly)]
        public void LockToPreventDelete_SingleFile()
        {
            string testFile1Contents = "TestContentsLockToPreventDelete \r\n";
            string testFile1Name = "test.txt";
            string testFile1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventDelete", testFile1Name));

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            using (FileStream testFile1 = File.Open(testFile1Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventDelete/" + testFile1Name);
            testFile1Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
        }

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(2)]
        [Category(Categories.WindowsOnly)]
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

            using (FileStream testFile2 = File.Open(testFile2Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFile3 = File.Open(testFile3Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFile4 = File.Open(testFile4Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventDelete/" + testFile2Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventDelete/" + testFile3Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventDelete/" + testFile4Name);

            testFile2Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile3Path.ShouldNotExistOnDisk(this.fileSystem);
            testFile4Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);
        }

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(3)]
        [Category(Categories.WindowsOnly)]
        public void LockToPreventUpdate_SingleFile()
        {
            string testFile1Contents = "Commit2LockToPreventUpdate \r\n";
            string testFile1OldContents = "TestFileLockToPreventUpdate \r\n";
            string testFile1Name = "test.txt";
            string testFile1Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "LockToPreventUpdate", testFile1Name));

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            using (FileStream testFile1 = File.Open(testFile1Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdate/" + testFile1Name);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1OldContents);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
        }

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(4)]
        [Category(Categories.WindowsOnly)]
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

            using (FileStream testFile2 = File.Open(testFile2Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFile3 = File.Open(testFile3Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFile4 = File.Open(testFile4Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdate/" + testFile2Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdate/" + testFile3Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdate/" + testFile4Name);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2OldContents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3OldContents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4OldContents);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
            testFile4Path.ShouldBeAFile(this.fileSystem).WithContents(testFile4Contents);
        }

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(5)]
        [Category(Categories.WindowsOnly)]
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

            using (FileStream testFileUpdate1 = File.Open(testFileUpdate1Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFileUpdate2 = File.Open(testFileUpdate2Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFileUpdate3 = File.Open(testFileUpdate3Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFileDelete1 = File.Open(testFileDelete1Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFileDelete2 = File.Open(testFileDelete2Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream testFileDelete3 = File.Open(testFileDelete3Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + OldCommitId);
                checkoutResult.Errors.ShouldContain(
                    "HEAD is now at " + OldCommitId,
                    "GVFS was unable to delete the following files. To recover, close all handles to the files and run these commands:",
                    "git clean -f " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete1Name,
                    "git clean -f " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete2Name,
                    "git clean -f " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete3Name,
                    "GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:",
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate1Name,
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate2Name,
                    "git checkout -- " + TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate3Name);

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

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate1Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate2Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate3Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete1Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete2Name);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileDelete3Name);

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

            if (this.CanUpdateAndDeletePlaceholdersWithOpenHandles())
            {
                using (FileStream testFileUpdate4 = File.Open(testFileUpdate4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (FileStream testFileDelete4 = File.Open(testFileDelete4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    this.GitCheckoutCommitId(OldCommitId);
                    this.GitStatusShouldBeClean(OldCommitId);
                }
            }
            else
            {
                using (FileStream testFileUpdate4Handle = File.Open(testFileUpdate4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (FileStream testFileDelete4Handle = File.Open(testFileDelete4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + OldCommitId);
                    checkoutResult.Errors.ShouldContain(
                        "HEAD is now at " + OldCommitId,
                        "GVFS was unable to update the following files. To recover, close all handles to the files and run these commands:",
                        "git checkout -- Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test4.txt");

                    GitHelpers.CheckGitCommandAgainstGVFSRepo(
                        this.Enlistment.RepoRoot,
                        "status",
                        "HEAD detached at " + OldCommitId,
                        "modified:   Test_EPF_UpdatePlaceholderTests/LockToPreventUpdateAndDelete/test4.txt",
                        "no changes added to commit (use \"git add\" and/or \"git commit -a\")\n");
                }

                this.GitCheckoutToDiscardChanges(TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate4Name);
                this.GitStatusShouldBeClean(OldCommitId);

                GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/LockToPreventUpdateAndDelete/" + testFileUpdate4Name);
            }

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

            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile1Name);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile2Name);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile3Name);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);
            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
        }

        // WindowsOnly because test requires Windows specific file sharing behavior
        [TestCase, Order(8)]
        [Category(Categories.WindowsOnly)]
        public void LockMoreThanMaxReportedFileNames()
        {
            string updateFilesFolder = "FilesToUpdate";
            string deleteFilesFolder = "FilesToDelete";

            for (int i = 1; i <= 51; ++i)
            {
                this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", updateFilesFolder, i.ToString() + ".txt")).ShouldBeAFile(this.fileSystem);
                this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", deleteFilesFolder, i.ToString() + ".txt")).ShouldBeAFile(this.fileSystem);
            }

            List<FileStream> openFiles = new List<FileStream>();
            try
            {
                for (int i = 1; i <= 51; ++i)
                {
                    FileStream file = File.Open(
                        this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", updateFilesFolder, i.ToString() + ".txt")),
                        FileMode.Open, 
                        FileAccess.Read,
                        FileShare.Read);
                    openFiles.Add(file);

                    file = File.Open(
                        this.Enlistment.GetVirtualPathTo(Path.Combine(TestParentFolderName, "MaxFileListCount", deleteFilesFolder, i.ToString() + ".txt")),
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    openFiles.Add(file);
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
                foreach (FileStream file in openFiles)
                {
                    file.Dispose();
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

        [TestCase, Order(9)]
        public void FullFilesDontAffectThePlaceholderDatabase()
        {
            string testFile = Path.Combine(this.Enlistment.RepoRoot, "FullFilesDontAffectThePlaceholderDatabase");

            string placeholderDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, "databases", "PlaceholderList.dat");
            string placeholdersBefore = GVFSHelpers.ReadAllTextFromWriteLockedFile(placeholderDatabase);
            
            this.fileSystem.CreateEmptyFile(testFile);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ReadAllTextFromWriteLockedFile(placeholderDatabase).ShouldEqual(placeholdersBefore);

            this.fileSystem.DeleteFile(testFile);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ReadAllTextFromWriteLockedFile(placeholderDatabase).ShouldEqual(placeholdersBefore);
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

        private bool CanUpdateAndDeletePlaceholdersWithOpenHandles()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // https://msdn.microsoft.com/en-us/library/windows/desktop/ms724429(v=vs.85).aspx
                FileVersionInfo kernel32Info = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));

                // 16248 is first build with support - see 12658248 for details
                if (kernel32Info.FileBuildPart >= 16248)
                {
                    return true;
                }

                return false;
            }

            return true;
        }
    }
}
