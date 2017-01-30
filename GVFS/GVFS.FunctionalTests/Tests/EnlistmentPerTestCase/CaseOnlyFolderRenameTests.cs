using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class CaseOnlyFolderRenameTests : TestsWithEnlistmentPerTestCase
    {
        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        [Ignore("Disabled until moving partial folders is supported")]
        public void CaseRenameFoldersAndRemountAndReanmeAgain(FileSystemRunner fileSystem)
        {
            // Projected folder without a physical folder
            string parentFolderName = "GVFS";
            string oldGVFSSubFolderName = "GVFS";
            string oldGVFSSubFolderPath = Path.Combine(parentFolderName, oldGVFSSubFolderName);
            string newGVFSSubFolderName = "gvfs";
            string newGVFSSubFolderPath = Path.Combine(parentFolderName, newGVFSSubFolderName);

            this.Enlistment.GetVirtualPathTo(oldGVFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(oldGVFSSubFolderName);

            // Use NativeMethods rather than the runner as it supports case-only rename
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(oldGVFSSubFolderPath), this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath));

            this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newGVFSSubFolderName);

            // Projected folder with a physical folder
            string oldTestsSubFolderName = "GVFS.FunctionalTests";
            string oldTestsSubFolderPath = Path.Combine(parentFolderName, oldTestsSubFolderName);
            string newTestsSubFolderName = "gvfs.functionaltests";
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
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newGVFSSubFolderName);
            this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(newTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(newTestsSubFolderPath, fileToAdd)).ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToAddContent);

            // Rename each folder again
            string finalGVFSSubFolderName = "gvFS";
            string finalGVFSSubFolderPath = Path.Combine(parentFolderName, finalGVFSSubFolderName);
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath), this.Enlistment.GetVirtualPathTo(finalGVFSSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalGVFSSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(finalGVFSSubFolderName);

            string finalTestsSubFolderName = "gvfs.FunctionalTESTS";
            string finalTestsSubFolderPath = Path.Combine(parentFolderName, finalTestsSubFolderName);
            NativeMethods.MoveFile(this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath), this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath).ShouldBeADirectory(fileSystem).WithCaseMatchingName(finalTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(finalTestsSubFolderPath, fileToAdd)).ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToAddContent);
        }
    }
}
