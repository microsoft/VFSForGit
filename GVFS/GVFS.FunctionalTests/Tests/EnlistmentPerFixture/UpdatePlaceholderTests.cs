using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
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

        [TestCase, Order(1)]
        [Category(Categories.LinuxTODO.NeedsContentionFreeFileLock)]
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

            using (FileStream testFileUpdate4 = File.Open(testFileUpdate4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (FileStream testFileDelete4 = File.Open(testFileDelete4Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                this.GitCheckoutCommitId(OldCommitId);
                this.GitStatusShouldBeClean(OldCommitId);
            }

            testFileUpdate4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate4OldContents);
            testFileDelete4Path.ShouldNotExistOnDisk(this.fileSystem);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);

            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);
            testFileUpdate4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileUpdate4Contents);
            testFileDelete4Path.ShouldBeAFile(this.fileSystem).WithContents(testFileDelete4Contents);
        }

        [TestCase, Order(2)]
        [Category(Categories.LinuxTODO.NeedsContentionFreeFileLock)]
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

            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile1Name);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile2Name);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, TestParentFolderName + "/FileProjectedAfterPlaceholderDeleteFileAndCheckout/" + testFile3Name);

            this.GitCheckoutCommitId(NewFilesAndChangesCommitId);
            this.GitStatusShouldBeClean(NewFilesAndChangesCommitId);

            testFile1Path.ShouldBeAFile(this.fileSystem).WithContents(testFile1Contents);
            testFile2Path.ShouldBeAFile(this.fileSystem).WithContents(testFile2Contents);
            testFile3Path.ShouldBeAFile(this.fileSystem).WithContents(testFile3Contents);
        }

        [TestCase, Order(3)]
        [Category(Categories.LinuxTODO.NeedsContentionFreeFileLock)]
        public void FullFilesDontAffectThePlaceholderDatabase()
        {
            string testFile = Path.Combine(this.Enlistment.RepoRoot, "FullFilesDontAffectThePlaceholderDatabase");

            string placeholderDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, "databases", "VFSForGit.sqlite");
            string placeholdersBefore = GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabase);

            this.fileSystem.CreateEmptyFile(testFile);

            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabase).ShouldEqual(placeholdersBefore);

            this.fileSystem.DeleteFile(testFile);

            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabase).ShouldEqual(placeholdersBefore);
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

        private void GitCheckoutToDiscardChanges(string gitPath)
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout -- " + gitPath);
        }

        private void GitCheckoutCommitId(string commitId)
        {
            this.InvokeGitAgainstGVFSRepo("checkout " + commitId).Errors.ShouldContain("HEAD is now at " + commitId);
        }
    }
}
