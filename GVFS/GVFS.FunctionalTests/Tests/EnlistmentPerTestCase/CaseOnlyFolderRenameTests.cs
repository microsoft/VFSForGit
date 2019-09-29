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
        private FileSystemRunner fileSystem;

        public CaseOnlyFolderRenameTests()
        {
            this.fileSystem = new BashRunner();
        }

        // Test applies only to platforms where renaming partial folders is allowed
        [TestCase]
        [Category(Categories.PartialFolderRenamesAllowed)]
        public void CaseRenameFoldersAndRemountAndRenameAgain()
        {
            // Projected folder without a physical folder
            string parentFolderName = "GVFS";
            string oldGVFSSubFolderName = "GVFS";
            string oldGVFSSubFolderPath = Path.Combine(parentFolderName, oldGVFSSubFolderName);
            string newGVFSSubFolderName = "gvfs";
            string newGVFSSubFolderPath = Path.Combine(parentFolderName, newGVFSSubFolderName);

            this.Enlistment.GetVirtualPathTo(oldGVFSSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(oldGVFSSubFolderName);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldGVFSSubFolderPath), this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath));

            this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(newGVFSSubFolderName);

            // Projected folder with a physical folder
            string oldTestsSubFolderName = "GVFS.FunctionalTests";
            string oldTestsSubFolderPath = Path.Combine(parentFolderName, oldTestsSubFolderName);
            string newTestsSubFolderName = "gvfs.functionaltests";
            string newTestsSubFolderPath = Path.Combine(parentFolderName, newTestsSubFolderName);

            string fileToAdd = "NewFile.txt";
            string fileToAddContent = "This is new file text.";
            string fileToAddPath = this.Enlistment.GetVirtualPathTo(Path.Combine(oldTestsSubFolderPath, fileToAdd));
            this.fileSystem.WriteAllText(fileToAddPath, fileToAddContent);

            this.Enlistment.GetVirtualPathTo(oldTestsSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(oldTestsSubFolderName);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldTestsSubFolderPath), this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath));

            this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(newTestsSubFolderName);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(newGVFSSubFolderName);
            this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(newTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(newTestsSubFolderPath, fileToAdd)).ShouldBeAFile(this.fileSystem).WithContents().ShouldEqual(fileToAddContent);

            // Rename each folder again
            string finalGVFSSubFolderName = "gvFS";
            string finalGVFSSubFolderPath = Path.Combine(parentFolderName, finalGVFSSubFolderName);
            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(newGVFSSubFolderPath), this.Enlistment.GetVirtualPathTo(finalGVFSSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalGVFSSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(finalGVFSSubFolderName);

            string finalTestsSubFolderName = "gvfs.FunctionalTESTS";
            string finalTestsSubFolderPath = Path.Combine(parentFolderName, finalTestsSubFolderName);
            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(newTestsSubFolderPath), this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath));
            this.Enlistment.GetVirtualPathTo(finalTestsSubFolderPath).ShouldBeADirectory(this.fileSystem).WithCaseMatchingName(finalTestsSubFolderName);
            this.Enlistment.GetVirtualPathTo(Path.Combine(finalTestsSubFolderPath, fileToAdd)).ShouldBeAFile(this.fileSystem).WithContents().ShouldEqual(fileToAddContent);
        }
    }
}
