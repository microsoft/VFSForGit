using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class SparseTests : TestsWithEnlistmentPerFixture
    {
        private static readonly string SparseAbortedMessage = Environment.NewLine + "Sparse was aborted.";
        private static readonly string[] NoSparseFolders = new string[0];
        private FileSystemRunner fileSystem = new SystemIORunner();
        private GVFSProcess gvfsProcess;
        private string mainSparseFolder = Path.Combine("GVFS", "GVFS");
        private string[] allDirectories;
        private string[] directoriesInMainFolder;

        [OneTimeSetUp]
        public void Setup()
        {
            this.gvfsProcess = new GVFSProcess(this.Enlistment);
            this.allDirectories = Directory.GetDirectories(this.Enlistment.RepoRoot, "*", SearchOption.AllDirectories)
                .Where(x => !x.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
                .ToArray();
            this.directoriesInMainFolder = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder));
        }

        [TearDown]
        public void TearDown()
        {
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            this.gvfsProcess.Sparse("--disable", shouldSucceed: true);

            // Remove all sparse folders should make all folders appear again
            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot, "*", SearchOption.AllDirectories)
                .Where(x => !x.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
                .ToArray();
            directories.ShouldMatchInOrder(this.allDirectories);
            this.ValidateFoldersInSparseList(NoSparseFolders);
        }

        [TestCase, Order(1)]
        public void BasicTestsAddingSparseFolder()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
            this.CheckMainSparseFolder();

            string secondPath = Path.Combine("GVFS", "GVFS.Common", "Physical");
            this.gvfsProcess.AddSparseFolders(secondPath);
            string folder = this.Enlistment.GetVirtualPathTo(secondPath);
            folder.ShouldBeADirectory(this.fileSystem);
            string file = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Common", "Enlistment.cs");
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

            string output = this.gvfsProcess.RemoveSparseFolders(shouldPrune: false, shouldSucceed: false, folders: "Scripts");
            output.ShouldContain(SparseAbortedMessage);
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

            string output = this.gvfsProcess.AddSparseFolders(shouldPrune: false, shouldSucceed: false, folders: this.mainSparseFolder);
            output.ShouldContain(SparseAbortedMessage);
            this.ValidateFoldersInSparseList(NoSparseFolders);
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

        [TestCase, Order(14)]
        public void ModifiedFileAndCommitThenChangingSparseFoldersWithPrune()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunFunctionalTests.bat");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.AddSparseFolders(shouldPrune: true, folders: this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, "Scripts");
            modifiedPath.ShouldNotExistOnDisk(this.fileSystem);
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(15)]
        public void PruneWithoutAnythingToPrune()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            this.gvfsProcess.PruneSparseNoFolders();
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
        }

        [TestCase, Order(16)]
        public void PruneAfterChanges()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderToCreateFileIn = Path.Combine("GVFS", "GVFS.Common");
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, folderToCreateFileIn);

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn, "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.RemoveSparseFolders(folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            this.gvfsProcess.PruneSparseNoFolders();
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn);
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToCreate.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(17)]
        public void PruneWithRemove()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderToCreateFileIn = Path.Combine("GVFS", "GVFS.Common");
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, folderToCreateFileIn);

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn, "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.gvfsProcess.RemoveSparseFolders(shouldPrune: true, folders: folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string folderPath = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn);
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToCreate.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(18)]
        public void ModifiedFileInSparseSetShouldAllowSparseFolderAdd()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.AddSparseFolders(folders: this.mainSparseFolder);
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
        }

        [TestCase, Order(19)]
        public void ModifiedFileOutsideSparseSetShouldNotAllowSparseFolderAdd()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.AddSparseFolders(shouldPrune: false, shouldSucceed: false, folders: "Scripts");
            output.ShouldContain("Running git status...Failed", SparseAbortedMessage);
            this.ValidateFoldersInSparseList(NoSparseFolders);
        }

        [TestCase, Order(20)]
        public void ModifiedFileInSparseSetShouldAllowSparseFolderRemove()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, "Scripts");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");

            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.RemoveSparseFolders(folders: "Scripts");
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
        }

        [TestCase, Order(21)]
        public void ModifiedFileOldSparseSetShouldNotAllowSparseFolderRemove()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, "Scripts");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");

            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.RemoveSparseFolders(shouldPrune: false, shouldSucceed: false, folders: this.mainSparseFolder);
            output.ShouldContain("Running git status...Failed", SparseAbortedMessage);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");
        }

        [TestCase, Order(22)]
        public void ModifiedFileInSparseSetShouldAllowPrune()
        {
            string additionalSparseFolder = Path.Combine("GVFS", "GVFS.Tests", "Should");
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, additionalSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, additionalSparseFolder);

            // Ensure that folderToCreateFileIn is on disk so that there's something to prune
            string folderToCreateFileIn = Path.Combine("GVFS", "GVFS.Common");
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder, additionalSparseFolder, folderToCreateFileIn);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, additionalSparseFolder, folderToCreateFileIn);

            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn, "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            // Modify a file that's in the sparse set (recursively)
            string modifiedFileContents = "New Contents";
            string modifiedPath = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS", "Program.cs");
            modifiedPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.WriteAllText(modifiedPath, modifiedFileContents);

            // Modify a file that is in the sparse set (via a non-recursive parent)
            string secondModifiedPath = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS.Tests", "NUnitRunner.cs");
            secondModifiedPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.WriteAllText(secondModifiedPath, modifiedFileContents);

            string expectedStatusOutput = GitProcess.Invoke(this.Enlistment.RepoRoot, "status --porcelain -uall");

            // Remove and prune folderToCreateFileIn
            string output = this.gvfsProcess.RemoveSparseFolders(shouldPrune: true, folders: folderToCreateFileIn);
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, additionalSparseFolder);

            // Confirm the prune succeeded
            string folderPath = Path.Combine(this.Enlistment.RepoRoot, folderToCreateFileIn);
            folderPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToCreate.ShouldNotExistOnDisk(this.fileSystem);

            // Confirm the changes to the modified file are preserved and that status does not change
            modifiedPath.ShouldBeAFile(this.fileSystem).WithContents(modifiedFileContents);
            secondModifiedPath.ShouldBeAFile(this.fileSystem).WithContents(modifiedFileContents);
            string statusOutput = GitProcess.Invoke(this.Enlistment.RepoRoot, "status --porcelain -uall");
            statusOutput.ShouldEqual(expectedStatusOutput, "Status output should not change.");
        }

        [TestCase, Order(23)]
        public void ModifiedFileInSparseSetShouldNotBeReportedWhenDirtyFilesOutsideSetPreventPrune()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            // Create a folder and file that will prevent pruning
            string newFolderName = "FolderOutsideSparse";
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(newFolderName));

            string newFileName = "newfile.txt";
            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(newFolderName, newFileName), "New Contents");

            // Modify a file that's in the sparse set, it should not be reported as dirty
            string modifiedFileName = "Program.cs";
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.SparseCommand(
                addFolders: false,
                shouldPrune: true,
                shouldSucceed: false,
                folders: Array.Empty<string>());
            output.ShouldContain("Running git status...Failed");
            output.ShouldContain($"{newFolderName}/{newFileName}");
            output.ShouldNotContain(ignoreCase: true, unexpectedSubstrings: modifiedFileName);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
        }

        [TestCase, Order(24)]
        public void GitStatusShouldNotRunWhenRemovingAllSparseFolders()
        {
            this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.RemoveSparseFolders(folders: this.mainSparseFolder);
            output.ShouldNotContain(ignoreCase: false, unexpectedSubstrings: "Running git status");
            this.ValidateFoldersInSparseList(NoSparseFolders);
        }

        [TestCase, Order(25)]
        public void GitStatusShouldRunWithFilesChangedInSparseSet()
        {
            string pathToChangeFiles = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "CommandLine");
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            this.fileSystem.WriteAllText(Path.Combine(pathToChangeFiles, "NewHelper.cs"), "New Contents");
            this.fileSystem.DeleteFile(Path.Combine(pathToChangeFiles, "CloneHelper.cs"));
            this.fileSystem.MoveFile(Path.Combine(pathToChangeFiles, "PrefetchHelper.cs"), Path.Combine(pathToChangeFiles, "PrefetchHelperRenamed.cs"));
            this.fileSystem.DeleteDirectory(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Properties"));
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");

            this.fileSystem.WriteAllText(Path.Combine(pathToChangeFiles, "NewVerb.cs"), "New Contents");
            this.fileSystem.WriteAllText(Path.Combine(pathToChangeFiles, "CloneVerb.cs"), "New Contents");
            this.fileSystem.DeleteFile(Path.Combine(pathToChangeFiles, "DiagnoseVerb.cs"));
            this.fileSystem.MoveFile(Path.Combine(pathToChangeFiles, "LogVerb.cs"), Path.Combine(pathToChangeFiles, "LogVerbRenamed.cs"));

            string expectedStatusOutput = GitProcess.Invoke(this.Enlistment.RepoRoot, "status --porcelain -uall");

            string output = this.gvfsProcess.AddSparseFolders(this.mainSparseFolder);
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            output = this.gvfsProcess.AddSparseFolders(folders: "Scripts");
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");

            output = this.gvfsProcess.RemoveSparseFolders(folders: "Scripts");
            output.ShouldContain("Running git status...Succeeded");
            this.ValidateFoldersInSparseList(this.mainSparseFolder);

            output = this.gvfsProcess.RemoveSparseFolders(folders: this.mainSparseFolder);
            output.ShouldNotContain(ignoreCase: false, unexpectedSubstrings: "Running git status");
            this.ValidateFoldersInSparseList(NoSparseFolders);

            output = this.gvfsProcess.AddSparseFolders(shouldPrune: false, shouldSucceed: false, folders: "Scripts");
            output.ShouldContain("Running git status...Failed", SparseAbortedMessage);
            this.ValidateFoldersInSparseList(NoSparseFolders);

            string statusOutput = GitProcess.Invoke(this.Enlistment.RepoRoot, "status --porcelain -uall");
            statusOutput.ShouldEqual(expectedStatusOutput, "Status output should not change.");
        }

        [TestCase, Order(26)]
        public void SetWithOtherOptionsFails()
        {
            string output = this.gvfsProcess.Sparse($"--set test --add test1", shouldSucceed: false);
            output.ShouldContain("--set not valid with other options.");
            output = this.gvfsProcess.Sparse($"--set test --remove test1", shouldSucceed: false);
            output.ShouldContain("--set not valid with other options.");
            output = this.gvfsProcess.Sparse($"--set test --file test1", shouldSucceed: false);
            output.ShouldContain("--set not valid with other options.");
        }

        [TestCase, Order(27)]
        public void FileWithOtherOptionsFails()
        {
            string output = this.gvfsProcess.Sparse($"--file test --add test1", shouldSucceed: false);
            output.ShouldContain("--file not valid with other options.");
            output = this.gvfsProcess.Sparse($"--file test --remove test1", shouldSucceed: false);
            output.ShouldContain("--file not valid with other options.");
            output = this.gvfsProcess.Sparse($"--file test --set test1", shouldSucceed: false);
            output.ShouldContain("--set not valid with other options.");
        }

        [TestCase, Order(28)]
        public void BasicSetOption()
        {
            this.gvfsProcess.Sparse($"--set {this.mainSparseFolder}", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
            this.CheckMainSparseFolder();
        }

        [TestCase, Order(29)]
        public void SetAddsAndRemovesFolders()
        {
            this.gvfsProcess.Sparse($"--set {this.mainSparseFolder};Scripts;", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");
            this.gvfsProcess.Sparse($"--set Scripts;GitCommandsTests", shouldSucceed: true);
            this.ValidateFoldersInSparseList("Scripts", "GitCommandsTests");
        }

        [TestCase, Order(30)]
        public void BasicFileOption()
        {
            string sparseFile = Path.Combine(this.Enlistment.EnlistmentRoot, "sparse-folders.txt");
            this.fileSystem.WriteAllText(sparseFile, this.mainSparseFolder);

            this.gvfsProcess.Sparse($"--file {sparseFile}", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder);
            this.CheckMainSparseFolder();
        }

        [TestCase, Order(31)]
        public void FileAddsAndRemovesFolders()
        {
            string sparseFile = Path.Combine(this.Enlistment.EnlistmentRoot, "sparse-folders.txt");
            this.fileSystem.WriteAllText(sparseFile, this.mainSparseFolder + Environment.NewLine + "Scripts");

            this.gvfsProcess.Sparse($"--file {sparseFile}", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, "Scripts");
            this.fileSystem.WriteAllText(sparseFile, "GitCommandsTests" + Environment.NewLine + "Scripts");
            this.gvfsProcess.Sparse($"--file {sparseFile}", shouldSucceed: true);
            this.ValidateFoldersInSparseList("Scripts", "GitCommandsTests");
        }

        [TestCase, Order(32)]
        public void DisableWithOtherOptionsFails()
        {
            string output = this.gvfsProcess.Sparse($"--disable --add test1", shouldSucceed: false);
            output.ShouldContain("--disable not valid with other options.");
            output = this.gvfsProcess.Sparse($"--disable --remove test1", shouldSucceed: false);
            output.ShouldContain("--disable not valid with other options.");
            output = this.gvfsProcess.Sparse($"--disable --set test1", shouldSucceed: false);
            output.ShouldContain("--disable not valid with other options.");
            output = this.gvfsProcess.Sparse($"--disable --file test1", shouldSucceed: false);
            output.ShouldContain("--disable not valid with other options.");
            output = this.gvfsProcess.Sparse($"--disable --prune", shouldSucceed: false);
            output.ShouldContain("--disable not valid with other options.");
        }

        [TestCase, Order(33)]
        public void DisableWhenNotInSparseModeShouldBeNoop()
        {
            this.ValidateFoldersInSparseList(NoSparseFolders);
            string output = this.gvfsProcess.Sparse("--disable", shouldSucceed: true);
            output.ShouldEqual(string.Empty);
            this.ValidateFoldersInSparseList(NoSparseFolders);
        }

        [TestCase, Order(34)]
        public void SetShouldFailIfModifiedFilesOutsideSparseSet()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            string output = this.gvfsProcess.Sparse($"--set Scripts", shouldSucceed: false);
            output.ShouldContain("Running git status...Failed", SparseAbortedMessage);
        }

        [TestCase, Order(35)]
        public void SetShouldFailIfModifiedFilesOutsideChangedSparseSet()
        {
            string secondFolder = Path.Combine("GVFS", "FastFetch");
            string output = this.gvfsProcess.Sparse($"--set {this.mainSparseFolder};{secondFolder}", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, secondFolder);
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder, "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            output = this.gvfsProcess.Sparse($"--set {secondFolder}", shouldSucceed: false);
            output.ShouldContain("Running git status...Failed", SparseAbortedMessage);
        }

        [TestCase, Order(36)]
        public void SetShouldSucceedIfModifiedFilesInChangedSparseSet()
        {
            string secondFolder = Path.Combine("GVFS", "FastFetch");
            string output = this.gvfsProcess.Sparse($"--set {this.mainSparseFolder};{secondFolder}", shouldSucceed: true);
            this.ValidateFoldersInSparseList(this.mainSparseFolder, secondFolder);
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, this.mainSparseFolder, "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");

            output = this.gvfsProcess.Sparse($"--set {this.mainSparseFolder}", shouldSucceed: true);
            output.ShouldContain("Running git status...Succeeded");
        }

        [TestCase, Order(37)]
        public void PruneShouldStillRunWhenSparseSetDidNotChange()
        {
            string modifiedPath = Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "Program.cs");
            this.fileSystem.WriteAllText(modifiedPath, "New Contents");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            string output = this.gvfsProcess.Sparse($"--set Scripts", shouldSucceed: true);
            output.ShouldContain("Running git status...Succeeded", "Updating sparse folder set...Succeeded", "Forcing a projection change...Succeeded");
            this.ValidateFoldersInSparseList("Scripts");
            modifiedPath.ShouldBeAFile(this.fileSystem);

            output = this.gvfsProcess.Sparse($"--set Scripts --prune", shouldSucceed: true);
            output.ShouldContain("No folders to update in sparse set.", "Found 1 folders to prune.", "Cleaning up folders...Succeeded", "GVFS folder prune successful.");
            this.ValidateFoldersInSparseList("Scripts");
            modifiedPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        private void CheckMainSparseFolder()
        {
            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.Length.ShouldEqual(2);
            directories.ShouldContain(x => x == Path.Combine(this.Enlistment.RepoRoot, ".git"));
            directories.ShouldContain(x => x == Path.Combine(this.Enlistment.RepoRoot, "GVFS"));

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
        }

        private void ValidatePathAddsAndRemoves(string path, string expectedSparsePath)
        {
            this.gvfsProcess.AddSparseFolders(path);
            this.ValidateFoldersInSparseList(expectedSparsePath);
            this.gvfsProcess.RemoveSparseFolders(path);
            this.ValidateFoldersInSparseList(NoSparseFolders);
            this.gvfsProcess.AddSparseFolders(path);
            this.ValidateFoldersInSparseList(expectedSparsePath);
            this.gvfsProcess.RemoveSparseFolders(expectedSparsePath);
            this.ValidateFoldersInSparseList(NoSparseFolders);
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
