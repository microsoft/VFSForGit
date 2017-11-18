using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class CaseOnlyFolderRenameTests : TestsWithEnlistmentPerTestCase
    {
        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        [Ignore("Disabled until moving partial folders is supported")]
        public void CaseRenameFoldersAndRemountAndReanmeAgain(FileSystemRunner fileSystem)
        {
            // Projected folder without a physical folder
            string parentFolderName = "RGFS";
            string oldRGFSSubFolderName = "RGFS";
            string oldRGFSSubFolderPath = Path.Combine(parentFolderName, oldRGFSSubFolderName);
            string newRGFSSubFolderName = "rgfs";
            string newRGFSSubFolderPath = Path.Combine(parentFolderName, newRGFSSubFolderName);

            this.Enlistment.GetVirtualPathTo(oldRGFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(oldRGFSSubFolderName);

            // Use NativeMethods rather than the runner as it supports case-only rename
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(oldRGFSSubFolderPath), this.Enlistment.GetVirtualPathTo(newRGFSSubFolderPath));

            this.Enlistment.GetVirtualPathTo(newRGFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newRGFSSubFolderName);

            // Projected folder with a physical folder
            string oldTestsSubFolderName = "RGFS.FunctionalTests";
            string oldTestsSubFolderPath = Path.Combine(parentFolderName, oldTestsSubFolderName);
            string newTestsSubFolderName = "rgfs.functionaltests";
            string newTestsSubFolderPath = Path.Combine(parentFolderName, newTestsSubFolderName);

            string fileToAdd = "NewFile.txt";
            string fileToAddContent = "This is new file text.";
            string fileToAddPath = this.Enlistment.GetVirtualPathTo(Path.Combine(oldTestsSubFolderPath, fileToAdd));
            fileSystem.WriteAllText(fileToAddPath, fileToAddContent);

            this.Enlistment.GetVirtualPathTo(oldTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(oldTestsSubFolderName);

            // Use NativeMethods rather than the runner as it supports case-only rename
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(oldTestsSubFolderPath), this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath));

            this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newTestsSubFolderName);

            // Remount
            this.Enlistment.UnmountRGFS();
            this.Enlistment.MountRGFS();

            this.Enlistment.GetVirtualPathTo(newRGFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newRGFSSubFolderName);
            this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(newTestsSubFolderPath, fileToAdd)).ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToAddContent);

            // Rename each folder again
            string finalRGFSSubFolderName = "rgFS";
            string finalRGFSSubFolderPath = Path.Combine(parentFolderName, finalRGFSSubFolderName);
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(newRGFSSubFolderPath), this.Enlistment.GetVirtualPathTo(finalRGFSSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalRGFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(finalRGFSSubFolderName);

            string finalTestsSubFolderName = "rgfs.FunctionalTESTS";
            string finalTestsSubFolderPath = Path.Combine(parentFolderName, finalTestsSubFolderName);
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath), this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(finalTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(finalTestsSubFolderPath, fileToAdd)).ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToAddContent);
        }
    }
}
