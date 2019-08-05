using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
            this.ValidateFoldersInSparseList(new string[0]);
        }

        [TestCase, Order(1)]
        public void BasicTestsAddingSparseFolder()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.Length.ShouldEqual(2);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, ".git"));
            directories[1].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, "GVFS"));

            string folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);

            string file = this.Enlistment.GetVirtualPathTo("Readme.md");
            file.ShouldBeAFile(this.fileSystem);

            folder = this.Enlistment.GetVirtualPathTo("Scripts");
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Mount");
            folder.ShouldNotExistOnDisk(this.fileSystem);

            string secondPath = Path.Combine("GVFS", "GVFS.Common", "Physical");
            this.gvfsProcess.AddSparseFolders(secondPath);
            folder = this.Enlistment.GetVirtualPathTo(secondPath);
            folder.ShouldBeADirectory(this.fileSystem);
            file = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Common", "Enlistment.cs");
            file.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(2)]
        public void AddAndRemoveVariousPathsTests()
        {
            // Paths to validate [0] = path to pass to sparse [1] = expected path saved
            string[][] paths = new[]
            {
                // AltDirectorySeparatorChar should get converted to DirectorySeparatorChar
                new[] { string.Join(Path.AltDirectorySeparatorChar.ToString(), "GVFS", "GVFS"), this.mainSparseFolder },

                // AltDirectorySeparatorChar should get trimmed
                new[] { $"{Path.AltDirectorySeparatorChar}{string.Join(Path.AltDirectorySeparatorChar.ToString(), "GVFS", "Test")}{Path.AltDirectorySeparatorChar}", Path.Combine("GVFS", "Test") },

                // DirectorySeparatorChar should get trimmed
                new[] { $"{Path.DirectorySeparatorChar}{Path.Combine("GVFS", "More")}{Path.DirectorySeparatorChar}", Path.Combine("GVFS", "More") },

                // spaces should get trimmed
                new[] { $" {string.Join(Path.AltDirectorySeparatorChar.ToString(), "GVFS", "Other")} ", Path.Combine("GVFS", "Other") },
            };

            foreach (string[] pathToValidate in paths)
            {
                this.ValidatePathAddsAndRemoves(pathToValidate[0], pathToValidate[1]);
            }
        }

        [TestCase, Order(3)]
        public void AddingParentDirectoryShouldMakeItRecursive()
        {
            string childPath = Path.Combine(this.mainSparseFolder, "CommandLine");
            this.gvfsProcess.AddSparseFolders(childPath);
            string[] directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
            directories.Length.ShouldEqual(1);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, childPath));
            this.ValidateFoldersInSparseList(childPath);

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
            directories.Length.ShouldBeAtLeast(2);
            directories.ShouldMatchInOrder(this.directoriesInMainFolder);
            this.ValidateFoldersInSparseList(childPath, this.mainSparseFolder);
        }

        [TestCase, Order(4)]
        public void AddingSiblingFolderShouldNotMakeParentRecursive()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            // Add and remove sibling folder to main folder
            string siblingPath = Path.Combine("GVFS", "FastFetch");
            this.gvfsProcess.AddSparseFolders(siblingPath);
            string folder = this.Enlistment.GetVirtualPathTo(siblingPath);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, siblingPath);

            this.gvfsProcess.RemoveSparseFolders(siblingPath);
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
        }

        [TestCase, Order(5)]
        public void AddingSubfolderShouldKeepParentRecursive()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            // Add subfolder of main folder and make sure it stays recursive
            string subFolder = Path.Combine(this.mainSparseFolder, "Properties");
            this.gvfsProcess.AddSparseFolders(subFolder);
            string folder = this.Enlistment.GetVirtualPathTo(subFolder);
            folder.ShouldBeADirectory(this.fileSystem);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, subFolder);

            folder = this.Enlistment.GetVirtualPathTo(this.mainSparseFolder, "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase, Order(6)]
        [Category(Categories.WindowsOnly)]
        public void CreatingFolderShouldAddToSparseListAndStartProjecting()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string newFolderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Common");
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Directory.CreateDirectory(newFolderPath);
            newFolderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(newFolderPath);
            fileSystemEntries.Length.ShouldEqual(32);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, Path.Combine("GVFS", "GVFS.Common"));

            string projectedFolder = Path.Combine(newFolderPath, "Git");
            projectedFolder.ShouldBeADirectory(this.fileSystem);
            fileSystemEntries = Directory.GetFileSystemEntries(projectedFolder);
            fileSystemEntries.Length.ShouldEqual(13);

            string projectedFile = Path.Combine(newFolderPath, "ReturnCode.cs");
            projectedFile.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(7)]
        [Category(Categories.MacOnly)]
        public void CreateFolderThenFileShouldAddToSparseListAndStartProjecting()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string newFolderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Common");
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Directory.CreateDirectory(newFolderPath);
            string newFilePath = Path.Combine(newFolderPath, "test.txt");
            File.WriteAllText(newFilePath, "New file content");
            newFolderPath.ShouldBeADirectory(this.fileSystem);
            newFilePath.ShouldBeAFile(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(newFolderPath);
            fileSystemEntries.Length.ShouldEqual(33);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, Path.Combine("GVFS", "GVFS.Common"));

            string projectedFolder = Path.Combine(newFolderPath, "Git");
            projectedFolder.ShouldBeADirectory(this.fileSystem);
            fileSystemEntries = Directory.GetFileSystemEntries(projectedFolder);
            fileSystemEntries.Length.ShouldEqual(13);

            string projectedFile = Path.Combine(newFolderPath, "ReturnCode.cs");
            projectedFile.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(7)]
        public void ReadFileThenChangingSparseFoldersShouldRemoveFileAndFolder()
        {
            string fileToRead = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.ReadAllText(fileToRead);

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(8)]
        public void CreateNewFileWillPreventRemoveSparseFolder()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, "Scripts");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");

            string output = this.gvfsProcess.RemoveSparseFolders(shouldSucceed: false, folders: "Scripts");
            output.ShouldContain("sparse was aborted");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(6);
            fileToCreate.ShouldBeAFile(this.fileSystem);

            this.fileSystem.DeleteFile(fileToCreate);
        }

        [TestCase, Order(9)]
        public void ModifiedFileShouldNotAllowSparseFolderChange()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.AddSparseFolders(shouldSucceed: false, folders: this.mainSparseFolder);
            output.ShouldContain("sparse was aborted");
            this.ValidateFoldersInSparseList(new string[0]);
        }

        [TestCase, Order(10)]
        public void ModifiedFileAndCommitThenChangingSparseFoldersShouldKeepFileAndFolder()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            folderPath.ShouldBeADirectory(this.fileSystem);
            modifiedPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(11)]
        public void DeleteFileAndCommitThenChangingSparseFoldersShouldKeepFolderAndFile()
        {
            string deletePath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Tests", "packages.config");
            this.fileSystem.DeleteFile(deletePath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            // File and folder should no longer be on disk because the file was deleted and the folder deleted becase it was empty
            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Tests");
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            deletePath.ShouldNotExistOnDisk(this.fileSystem);

            // Folder and file should be on disk even though they are outside the sparse scope because the file is in the modified paths
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout HEAD~1");
            folderPath.ShouldBeADirectory(this.fileSystem);
            deletePath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(12)]
        public void CreateNewFileAndCommitThenRemoveSparseFolderShouldKeepFileAndFolder()
        {
            string folderToCreateFileIn = Path.Combine("GVFS", "GVFS.Hooks");
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, folderToCreateFileIn);

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn, "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.RemoveSparseFolders(folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn);
            folderPath.ShouldBeADirectory(this.fileSystem);
            string[] fileSystemEntries = Directory.GetFileSystemEntries(folderPath);
            fileSystemEntries.Length.ShouldEqual(1);
            fileToCreate.ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(13)]
        [Category(Categories.MacOnly)]
        public void CreateFolderAndFileThatAreExcluded()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

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

        private void ValidatePathAddsAndRemoves(string path, string expectedSparsePath)
        {
            this.gvfsProcess.AddSparseFolders(path);
            this.ValidateFoldersInSparseList(expectedSparsePath);
            this.gvfsProcess.RemoveSparseFolders(path);
            this.ValidateFoldersInSparseList(new string[0]);
            this.gvfsProcess.AddSparseFolders(path);
            this.ValidateFoldersInSparseList(expectedSparsePath);
            this.gvfsProcess.RemoveSparseFolders(expectedSparsePath);
            this.ValidateFoldersInSparseList(new string[0]);
        }

        private void ValidateFoldersInSparseList(params string[] folders)
        {
            StringBuilder folderErrors = new StringBuilder();
            HashSet<string> actualSparseFolders = new HashSet<string>(this.gvfsProcess.GetSparseFolders());

            foreach (string expectedFolder in folders)
            {
                if (!actualSparseFolders.Contains(expectedFolder))
                {
                    folderErrors.AppendLine($"{expectedFolder} not found in actual folder list");
                }

                actualSparseFolders.Remove(expectedFolder);
            }

            foreach (string extraFolder in actualSparseFolders)
            {
                folderErrors.AppendLine($"{extraFolder} unexpected in folder list");
            }

            folderErrors.Length.ShouldEqual(0, folderErrors.ToString());
        }
    }
}
