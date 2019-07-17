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
    public class IncludedFolderTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem = new SystemIORunner();
        private GVFSProcess gvfsProcess;
        private string mainIncludedFolder = Path.Combine("GVFS", "GVFS");
        private string[] allRootDirectories;
        private string[] directoriesInMainFolder;

        [OneTimeSetUp]
        public void Setup()
        {
            this.gvfsProcess = new GVFSProcess(this.Enlistment);
            this.allRootDirectories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            this.directoriesInMainFolder = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainIncludedFolder));
        }

        [TearDown]
        public void TearDown()
        {
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            foreach (string includedFolder in this.gvfsProcess.GetIncludedFolders())
            {
                this.gvfsProcess.RemoveIncludedFolders(includedFolder);
            }

            // Remove all included folders should make all folders appear again
            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.ShouldMatchInOrder(this.allRootDirectories);
            this.ValidateIncludedFolders(new string[0]);
        }

        [TestCase, Order(1)]
        public void BasicTestsAddingAndRemoving()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.Length.ShouldEqual(2);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, ".git"));
            directories[1].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, "GVFS"));

            string folder = this.Enlistment.GetVirtualPathTo(this.mainIncludedFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainIncludedFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);

            folder = this.Enlistment.GetVirtualPathTo("Scripts");
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Common");
            folder.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(2)]
        public void AddingParentDirectoryShouldMakeItRecursive()
        {
            string childPath = Path.Combine(this.mainIncludedFolder, "CommandLine");
            this.gvfsProcess.AddIncludedFolders(childPath);
            string[] directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainIncludedFolder));
            directories.Length.ShouldEqual(1);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, childPath));
            this.ValidateIncludedFolders(childPath);

            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainIncludedFolder));
            directories.ShouldMatchInOrder(this.directoriesInMainFolder);
            this.ValidateIncludedFolders(childPath, this.mainIncludedFolder);
        }

        [TestCase, Order(3)]
        public void AddingSiblingFolderShouldNotMakeParentRecursive()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            // Add and remove sibling folder to main folder
            string siblingPath = Path.Combine("GVFS", "FastFetch");
            this.gvfsProcess.AddIncludedFolders(siblingPath);
            string folder = this.Enlistment.GetVirtualPathTo(siblingPath);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateIncludedFolders(this.mainIncludedFolder, siblingPath);

            this.gvfsProcess.RemoveIncludedFolders(siblingPath);
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainIncludedFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateIncludedFolders(this.mainIncludedFolder);
        }

        [TestCase, Order(4)]
        public void AddingSubfolderShouldKeepParentRecursive()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            // Add subfolder of main folder and make sure it stays recursive
            string subFolder = Path.Combine(this.mainIncludedFolder, "Properties");
            this.gvfsProcess.AddIncludedFolders(subFolder);
            string folder = this.Enlistment.GetVirtualPathTo(subFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateIncludedFolders(this.mainIncludedFolder, subFolder);

            folder = this.Enlistment.GetVirtualPathTo(this.mainIncludedFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase, Order(5)]
        [Category(Categories.WindowsOnly)]
        public void CreatingFolderShouldAddToIncludedListAndStartProjecting()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

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
        public void CreateFolderThenFileShouldAddToIncludedListAndStartProjecting()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

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
        public void ReadFileThenChangingIncludeFoldersShouldRemoveFileAndFolder()
        {
            string fileToRead = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.ReadAllText(fileToRead);

            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(7)]
        public void CreateNewFileWillPreventRemoveIncludedFolder()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder, "Scripts");
            this.ValidateIncludedFolders(this.mainIncludedFolder, "Scripts");

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");

            this.gvfsProcess.RemoveIncludedFolders("Scripts");
            this.ValidateIncludedFolders(this.mainIncludedFolder, "Scripts");

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(6);
            fileToCreate.ShouldBeAFile(this.fileSystem);

            this.fileSystem.DeleteFile(fileToCreate);
        }

        [TestCase, Order(8)]
        public void ModifiedFileShouldNotAllowIncludedFolderChange()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            output.ShouldContain("Include was aborted");
            this.ValidateIncludedFolders(new string[0]);
        }

        [TestCase, Order(9)]
        public void ModifiedFileAndCommitThenChangingIncludeFoldersShouldKeepFileAndFolder()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder);
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            modifiedPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(10)]
        public void CreateNewFileAndCommitThenRemoveIncludedFolderShouldKeepFileAndFolder()
        {
            this.gvfsProcess.AddIncludedFolders(this.mainIncludedFolder, "Scripts");
            this.ValidateIncludedFolders(this.mainIncludedFolder, "Scripts");

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.RemoveIncludedFolders("Scripts");
            this.ValidateIncludedFolders(this.mainIncludedFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(2);
            fileToCreate.ShouldBeAFile(this.fileSystem);
        }

        private void ValidateIncludedFolders(params string[] folders)
        {
            HashSet<string> actualIncludedFolders = new HashSet<string>(this.gvfsProcess.GetIncludedFolders());
            folders.Length.ShouldEqual(actualIncludedFolders.Count);
            foreach (string expectedFolder in folders)
            {
                actualIncludedFolders.Contains(expectedFolder).ShouldBeTrue($"{expectedFolder} not found in actual folder list");
            }
        }
    }
}
