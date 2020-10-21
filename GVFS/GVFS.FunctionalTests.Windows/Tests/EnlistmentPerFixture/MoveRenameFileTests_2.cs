using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    // TODO 452590 - Combine all of the MoveRenameTests into a single fixture, and have each use different
    // well known files
    [TestFixtureSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
    public class MoveRenameFileTests_2 : TestsWithEnlistmentPerFixture
    {
        private const string TestFileFolder = "Test_EPF_MoveRenameFileTests_2";

        // Test_EPF_MoveRenameFileTests_2\RunUnitTests.bat
        private const string RunUnitTestsContents =
@"@ECHO OFF
IF ""%1""=="""" (SET ""Configuration=Debug"") ELSE (SET ""Configuration=%1"")

%~dp0\..\..\BuildOutput\GVFS.UnitTests\bin\x64\%Configuration%\GVFS.UnitTests.exe";

        // Test_EPF_MoveRenameFileTests_2\RunFunctionalTests.bat
        private const string RunFunctioanlTestsContents =
@"@ECHO OFF
IF ""%1""=="""" (SET ""Configuration=Debug"") ELSE (SET ""Configuration=%1"")

%~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\GVFS.FunctionalTests.exe %2";

        private FileSystemRunner fileSystem;

        public MoveRenameFileTests_2(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        // This test needs the GVFS folder to not exist on physical disk yet, so run it first
        [TestCase, Order(1)]
        public void MoveUnhydratedFileToUnhydratedFolderAndWrite()
        {
            string testFileContents = RunUnitTestsContents;
            string testFileName = "RunUnitTests.bat";

            // Assume there will always be a GVFS folder when running tests
            string testFolderName = "GVFS";

            string oldTestFileVirtualPath = this.Enlistment.GetVirtualPathTo(TestFileFolder, testFileName);
            string newTestFileVirtualPath = this.Enlistment.GetVirtualPathTo(testFolderName, testFileName);

            this.fileSystem.MoveFile(oldTestFileVirtualPath, newTestFileVirtualPath);
            oldTestFileVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(testFileContents);
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldBeADirectory(this.fileSystem);

            // Writing after the move should succeed
            string newText = "New file text for test file";
            this.fileSystem.WriteAllText(newTestFileVirtualPath, newText);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(newText);
        }

        [TestCase, Order(2)]
        public void MoveUnhydratedFileToNewFolderAndWrite()
        {
            string testFolderName = "test_folder";
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldBeADirectory(this.fileSystem);

            string testFileName = "RunFunctionalTests.bat";
            string testFileContents = RunFunctioanlTestsContents;

            string newTestFileVirtualPath = Path.Combine(this.Enlistment.GetVirtualPathTo(testFolderName), testFolderName);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(TestFileFolder, testFileName), newTestFileVirtualPath);
            this.Enlistment.GetVirtualPathTo(TestFileFolder, testFileName).ShouldNotExistOnDisk(this.fileSystem);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(testFileContents);

            // Writing after the move should succeed
            string newText = "New file text for test file";
            this.fileSystem.WriteAllText(newTestFileVirtualPath, newText);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(newText);

            this.fileSystem.DeleteFile(newTestFileVirtualPath);
            newTestFileVirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(testFolderName));
            this.Enlistment.GetVirtualPathTo(testFolderName).ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(3)]
        public void MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite()
        {
            string targetFilename = Path.Combine(TestFileFolder, "MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite", "RunFunctionalTests.bat");
            string sourceFilename = Path.Combine(TestFileFolder, "MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite", "RunUnitTests.bat");
            string sourceFileContents = RunUnitTestsContents;

            // Overwriting one unhydrated file with another should create a file at the target
            this.fileSystem.ReplaceFile(this.Enlistment.GetVirtualPathTo(sourceFilename), this.Enlistment.GetVirtualPathTo(targetFilename));
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(sourceFileContents);

            // Source file should be gone
            this.Enlistment.GetVirtualPathTo(sourceFilename).ShouldNotExistOnDisk(this.fileSystem);

            // Writing after move should succeed
            string newText = "New file text for target file";
            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(targetFilename), newText);
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(newText);
        }

        [TestCase, Order(4)]
        public void CaseOnlyRenameFileInSubfolder()
        {
            string oldFilename = "CaseOnlyRenameFileInSubfolder.txt";
            string oldVirtualPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFileFolder, oldFilename));
            oldVirtualPath.ShouldBeAFile(this.fileSystem).WithCaseMatchingName(oldFilename);

            string newFilename = "caseonlyrenamefileinsubfolder.txt";
            string newVirtualPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFileFolder, newFilename));

            // Rename file, and confirm file name case was updated
            this.fileSystem.MoveFile(oldVirtualPath, newVirtualPath);
            newVirtualPath.ShouldBeAFile(this.fileSystem).WithCaseMatchingName(newFilename);
        }

        [TestCase, Order(5)]
        public void MoveUnhydratedFileToOverwriteFullFileAndWrite()
        {
            string targetFilename = "TargetFile.txt";
            string targetFileContents = "The Target";

            string sourceFilename = Path.Combine(
                TestFileFolder,
                "MoveUnhydratedFileToOverwriteFullFileAndWrite",
                "MoveUnhydratedFileToOverwriteFullFileAndWrite.txt");

            string sourceFileContents =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  <package id=""NUnit"" version=""3.5.0"" targetFramework=""net452"" />
  <package id=""NUnitLite"" version=""3.5.0"" targetFramework=""net452"" />
</packages>";

            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(targetFilename), targetFileContents);
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(targetFileContents);

            // Overwriting a virtual NTFS file with an unprojected file should leave a file on disk at the
            // target location
            this.fileSystem.ReplaceFile(this.Enlistment.GetVirtualPathTo(sourceFilename), this.Enlistment.GetVirtualPathTo(targetFilename));
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(sourceFileContents);

            // Source file should be gone
            this.Enlistment.GetVirtualPathTo(sourceFilename).ShouldNotExistOnDisk(this.fileSystem);

            // Writes should succeed after move
            string newText = "New file text for Readme.md";
            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(targetFilename), newText);
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(newText);
        }
    }
}
