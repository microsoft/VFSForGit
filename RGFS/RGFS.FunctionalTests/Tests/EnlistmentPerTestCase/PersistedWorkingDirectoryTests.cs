using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class PersistedWorkingDirectoryTests : TestsWithEnlistmentPerTestCase
    {
        public void MountMatchesRemount()
        {
            List<string> fileEntriesBefore = new List<string>(Directory.EnumerateFileSystemEntries(this.Enlistment.RepoRoot));

            this.Enlistment.UnmountRGFS();
            this.Enlistment.MountRGFS();

            List<string> fileEntriesAfter = new List<string>(Directory.EnumerateFileSystemEntries(this.Enlistment.RepoRoot));

            fileEntriesBefore.Count.ShouldEqual(fileEntriesAfter.Count);
            fileEntriesBefore.ShouldContain(fileEntriesAfter, (item, expectedValue) => { return string.Equals(item, expectedValue); });
        }

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PersistedDirectoryLazyLoad(FileSystemRunner fileSystem)
        {
            const string EnumerateDirectoryName = "RGFS\\RGFS";

            string[] subFolders = new string[]
            {
                Path.Combine(EnumerateDirectoryName, "Properties"),
                Path.Combine(EnumerateDirectoryName, "CommandLine")
            };

            string[] subFiles = new string[]
            {
                Path.Combine(EnumerateDirectoryName, "App.config"),
                Path.Combine(EnumerateDirectoryName, "GitVirtualFileSystem.ico"),
                Path.Combine(EnumerateDirectoryName, "RGFS.csproj"),
                Path.Combine(EnumerateDirectoryName, "packages.config"),
                Path.Combine(EnumerateDirectoryName, "Program.cs"),
                Path.Combine(EnumerateDirectoryName, "Setup.iss")
            };

            string enumerateDirectoryPath = this.Enlistment.GetVirtualPathTo(EnumerateDirectoryName);
            fileSystem.DirectoryExists(enumerateDirectoryPath).ShouldEqual(true);
            
            foreach (string folder in subFolders)
            {
                string directoryPath = this.Enlistment.GetVirtualPathTo(folder);
                fileSystem.DirectoryExists(directoryPath).ShouldEqual(true);
            }

            foreach (string file in subFiles)
            {
                string filePath = this.Enlistment.GetVirtualPathTo(file);
                fileSystem.FileExists(filePath).ShouldEqual(true);
            }

            this.Enlistment.UnmountRGFS();
            this.Enlistment.MountRGFS();

            foreach (string folder in subFolders)
            {
                string directoryPath = this.Enlistment.GetVirtualPathTo(folder);
                fileSystem.DirectoryExists(directoryPath).ShouldEqual(true);
            }

            foreach (string file in subFiles)
            {
                string filePath = this.Enlistment.GetVirtualPathTo(file);
                fileSystem.FileExists(filePath).ShouldEqual(true);
            }
        }

        /// <summary>
        /// This test is intentionally one monolithic test. Because we have to mount/remount to
        /// test persistence, we want to save as much time in tests runs as possible by only
        /// remounting once.
        /// </summary>
        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PersistedDirectoryTests(FileSystemRunner fileSystem)
        {
            // Delete File Setup
            string deleteFileName = ".gitattributes";
            string deleteFilepath = this.Enlistment.GetVirtualPathTo(deleteFileName);
            fileSystem.DeleteFile(deleteFilepath);

            // Delete Folder Setup
            string deleteFolderName = "RGFS\\RGFS";
            string deleteFolderPath = this.Enlistment.GetVirtualPathTo(deleteFolderName);
            fileSystem.DeleteDirectory(deleteFolderPath);

            // Add File Setup
            string fileToAdd = "NewFile.txt";
            string fileToAddContent = "This is new file text.";
            string fileToAddPath = this.Enlistment.GetVirtualPathTo(fileToAdd);
            fileSystem.WriteAllText(fileToAddPath, fileToAddContent);

            // Add Folder Setup
            string directoryToAdd = "NewDirectory";
            string directoryToAddPath = this.Enlistment.GetVirtualPathTo(directoryToAdd);
            fileSystem.CreateDirectory(directoryToAddPath);

            // Move File Setup
            string fileToMove = this.Enlistment.GetVirtualPathTo("FileToMove.txt");
            string fileToMoveNewPath = this.Enlistment.GetVirtualPathTo("MovedFile.txt");
            string fileToMoveContent = "This is new file text.";
            fileSystem.WriteAllText(fileToMove, fileToMoveContent);
            fileSystem.MoveFile(fileToMove, fileToMoveNewPath);

            // Replace File Setup
            string fileToReplace = this.Enlistment.GetVirtualPathTo("FileToReplace.txt");
            string fileToReplaceNewPath = this.Enlistment.GetVirtualPathTo("ReplacedFile.txt");
            string fileToReplaceContent = "This is new file text.";
            string fileToReplaceOldContent = "This is very different file text.";
            fileSystem.WriteAllText(fileToReplace, fileToReplaceContent);
            fileSystem.WriteAllText(fileToReplaceNewPath, fileToReplaceOldContent);
            fileSystem.ReplaceFile(fileToReplace, fileToReplaceNewPath);

            // MoveFolderPersistsOnRemount Setup
            string directoryToMove = this.Enlistment.GetVirtualPathTo("MoveDirectory");
            string directoryMoveTarget = this.Enlistment.GetVirtualPathTo("MoveDirectoryTarget");
            string newDirectory = Path.Combine(directoryMoveTarget, "MoveDirectory_renamed");
            string childFile = Path.Combine(directoryToMove, "MoveFile.txt");
            string movedChildFile = Path.Combine(newDirectory, "MoveFile.txt");
            string moveFileContents = "This text file is getting moved";
            fileSystem.CreateDirectory(directoryToMove);
            fileSystem.CreateDirectory(directoryMoveTarget);
            fileSystem.WriteAllText(childFile, moveFileContents);
            fileSystem.MoveDirectory(directoryToMove, newDirectory);

            // NestedLoadAndWriteAfterMount Setup
            // Write a file to RGFS to ensure it has a physical folder
            string childFileToAdd = "RGFS\\ChildFileToAdd.txt";
            string childFileToAddContent = "This is new child file in the RGFS folder.";
            string childFileToAddPath = this.Enlistment.GetVirtualPathTo(childFileToAdd);
            fileSystem.WriteAllText(childFileToAddPath, childFileToAddContent);              

            // Remount
            this.Enlistment.UnmountRGFS();
            this.Enlistment.MountRGFS();

            // Delete File Validation
            deleteFilepath.ShouldNotExistOnDisk(fileSystem);

            // Delete Folder Validation
            deleteFolderPath.ShouldNotExistOnDisk(fileSystem);

            // Add File Validation
            fileToAddPath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToAddContent);

            // Add Folder Validation
            directoryToAddPath.ShouldBeADirectory(fileSystem);

            // Move File Validation
            fileToMove.ShouldNotExistOnDisk(fileSystem);
            fileToMoveNewPath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToMoveContent);

            // Replace File Validation
            fileToReplace.ShouldNotExistOnDisk(fileSystem);
            fileToReplaceNewPath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(fileToReplaceContent);

            // MoveFolderPersistsOnRemount Validation
            directoryToMove.ShouldNotExistOnDisk(fileSystem);

            directoryMoveTarget.ShouldBeADirectory(fileSystem);
            newDirectory.ShouldBeADirectory(fileSystem);
            movedChildFile.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(moveFileContents);

            // NestedLoadAndWriteAfterMount Validation
            childFileToAddPath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(childFileToAddContent);
            string childFolder = "RGFS\\RGFS.FunctionalTests";
            string childFolderPath = this.Enlistment.GetVirtualPathTo(childFolder);
            childFolderPath.ShouldBeADirectory(fileSystem);
            string postMountChildFile = "PostMountChildFile.txt";
            string postMountChildFileContent = "This is new child file added after the mount";
            string postMountChildFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(childFolder, postMountChildFile));
            fileSystem.WriteAllText(postMountChildFilePath, postMountChildFileContent); // Verify we can create files in subfolders of RGFS  
            postMountChildFilePath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(postMountChildFileContent);

            // 663045 - Ensure that folder can be deleted after a new file is added and RGFS is remounted
            fileSystem.DeleteDirectory(childFolderPath);
            childFolderPath.ShouldNotExistOnDisk(fileSystem);
        }        
    }
}