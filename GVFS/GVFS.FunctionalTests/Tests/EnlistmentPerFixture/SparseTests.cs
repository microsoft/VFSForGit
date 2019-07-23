using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class SparseTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem = new SystemIORunner();
        private GVFSProcess gvfsProcess;
        private string mainSparseFolder = Path.Combine("GVFS", "GVFS");
        private string[] allRootDirectories;
        private string[] directoriesInMainFolder;

        [OneTimeSetUp]
        public void Setup()
        {
            this.gvfsProcess = new GVFSProcess(this.Enlistment);
            this.allRootDirectories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            this.directoriesInMainFolder = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
        }

        [TearDown]
        public void TearDown()
        {
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            foreach (string sparseFolder in this.gvfsProcess.GetSparseFolders())
            {
                this.gvfsProcess.RemoveSparseFolders(sparseFolder);
            }

            // Remove all sparse folders should make all folders appear again
            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.ShouldMatchInOrder(this.allRootDirectories);
            this.ValidateSparseFolders(new string[0]);
        }

        [TestCase, Order(1)]
        public void BasicTestsAddingAndRemoving()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.Length.ShouldEqual(2);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, ".git"));
            directories[1].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, "GVFS"));

            string folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);

            folder = this.Enlistment.GetVirtualPathTo("Scripts");
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Common");
            folder.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(2)]
        public void AddingParentDirectoryShouldMakeItRecursive()
        {
            string childPath = Path.Combine(this.mainSparseFolder, "CommandLine");
            this.gvfsProcess.AddSparseFolders(childPath);
            string[] directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
            directories.Length.ShouldEqual(1);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, childPath));
            this.ValidateSparseFolders(childPath);

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
            directories.ShouldMatchInOrder(this.directoriesInMainFolder);
            this.ValidateSparseFolders(childPath, this.mainSparseFolder);
        }

        [TestCase, Order(3)]
        public void AddingSiblingFolderShouldNotMakeParentRecursive()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            // Add and remove sibling folder to main folder
            string siblingPath = Path.Combine("GVFS", "FastFetch");
            this.gvfsProcess.AddSparseFolders(siblingPath);
            string folder = this.Enlistment.GetVirtualPathTo(siblingPath);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateSparseFolders(this.mainSparseFolder, siblingPath);

            this.gvfsProcess.RemoveSparseFolders(siblingPath);
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateSparseFolders(this.mainSparseFolder);
        }

        [TestCase, Order(4)]
        public void AddingSubfolderShouldKeepParentRecursive()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            // Add subfolder of main folder and make sure it stays recursive
            string subFolder = Path.Combine(this.mainSparseFolder, "Properties");
            this.gvfsProcess.AddSparseFolders(subFolder);
            string folder = this.Enlistment.GetVirtualPathTo(subFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateSparseFolders(this.mainSparseFolder, subFolder);

            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase, Order(5)]
        [Category(Categories.WindowsOnly)]
        public void CreatingFolderShouldAddToSparseListAndStartProjecting()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            string newFolderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Common");
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Directory.CreateDirectory(newFolderPath);
            newFolderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(newFolderPath);
            fileSystemEntries.Length.ShouldEqual(32);

            string projectedFolder = Path.Combine(newFolderPath, "Git");
            projectedFolder.ShouldBeADirectory(this.fileSystem);
            fileSystemEntries = Directory.GetFileSystemEntries(projectedFolder);
            fileSystemEntries.Length.ShouldEqual(13);

            string projectedFile = Path.Combine(newFolderPath, "ReturnCode.cs");
            projectedFile.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(5)]
        [Category(Categories.MacOnly)]
        public void CreateFolderThenFileShouldAddToSparseListAndStartProjecting()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            string newFolderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Common");
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Directory.CreateDirectory(newFolderPath);
            string newFilePath = Path.Combine(newFolderPath, "test.txt");
            File.WriteAllText(newFilePath, "New file content");
            newFolderPath.ShouldBeADirectory(this.fileSystem);
            newFilePath.ShouldBeAFile(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(newFolderPath);
            fileSystemEntries.Length.ShouldEqual(33);

            string projectedFolder = Path.Combine(newFolderPath, "Git");
            projectedFolder.ShouldBeADirectory(this.fileSystem);
            fileSystemEntries = Directory.GetFileSystemEntries(projectedFolder);
            fileSystemEntries.Length.ShouldEqual(13);

            string projectedFile = Path.Combine(newFolderPath, "ReturnCode.cs");
            projectedFile.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(6)]
        public void ReadFileThenChangingSparseFoldersShouldRemoveFileAndFolder()
        {
            string fileToRead = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.ReadAllText(fileToRead);

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(7)]
        public void CreateNewFileWillPreventRemoveSparseFolder()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, "Scripts");
            this.ValidateSparseFolders(this.mainSparseFolder, "Scripts");

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");

            this.gvfsProcess.RemoveSparseFolders("Scripts");
            this.ValidateSparseFolders(this.mainSparseFolder, "Scripts");

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(6);
            fileToCreate.ShouldBeAFile(this.fileSystem);

            this.fileSystem.DeleteFile(fileToCreate);
        }

        [TestCase, Order(8)]
        public void ModifiedFileShouldNotAllowSparseFolderChange()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            output.ShouldContain("sparse was aborted");
            this.ValidateSparseFolders(new string[0]);
        }

        [TestCase, Order(9)]
        public void ModifiedFileAndCommitThenChangingSparseFoldersShouldKeepFileAndFolder()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            modifiedPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(10)]
        public void CreateNewFileAndCommitThenRemoveSparseFolderShouldKeepFileAndFolder()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, "Scripts");
            this.ValidateSparseFolders(this.mainSparseFolder, "Scripts");

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.RemoveSparseFolders("Scripts");
            this.ValidateSparseFolders(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(2);
            fileToCreate.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(11)]
        [Category(Categories.MacOnly)]
        public void CreateFolderAndFileThatAreExcluded()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateSparseFolders(this.mainSparseFolder);

            // Create a file that should already be in the projection but excluded
            string newFolderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Mount");
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Directory.CreateDirectory(newFolderPath);
            string newFilePath = Path.Combine(newFolderPath, "Program.cs");
            File.WriteAllText(newFilePath, "New file content");
            newFolderPath.ShouldBeADirectory(this.fileSystem);
            newFilePath.ShouldBeAFile(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(newFolderPath);
            fileSystemEntries.Length.ShouldEqual(7);

            string projectedFolder = Path.Combine(newFolderPath, "Properties");
            projectedFolder.ShouldBeADirectory(this.fileSystem);
            fileSystemEntries = Directory.GetFileSystemEntries(projectedFolder);
            fileSystemEntries.Length.ShouldEqual(1);

            string projectedFile = Path.Combine(newFolderPath, "MountVerb.cs");
            projectedFile.ShouldBeAFile(this.fileSystem);
        }

        private void ValidateSparseFolders(params string[] folders)
        {
            HashSet<string> actualSparseFolders = new HashSet<string>(this.gvfsProcess.GetSparseFolders());
            folders.Length.ShouldEqual(actualSparseFolders.Count);
            foreach (string expectedFolder in folders)
            {
                actualSparseFolders.Contains(expectedFolder).ShouldBeTrue($"{expectedFolder} not found in actual folder list");
            }
        }
    }
}
