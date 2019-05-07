using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class PersistedWorkingDirectoryTests : TestsWithEnlistmentPerTestCase
    {
        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void PersistedDirectoryLazyLoad(FileSystemRunner fileSystem)
        {
            string enumerateDirectoryName = Path.Combine("GVFS", "GVFS");

            string[] subFolders = new string[]
            {
                Path.Combine(enumerateDirectoryName, "Properties"),
                Path.Combine(enumerateDirectoryName, "CommandLine")
            };

            string[] subFiles = new string[]
            {
                Path.Combine(enumerateDirectoryName, "App.config"),
                Path.Combine(enumerateDirectoryName, "GitVirtualFileSystem.ico"),
                Path.Combine(enumerateDirectoryName, "GVFS.csproj"),
                Path.Combine(enumerateDirectoryName, "packages.config"),
                Path.Combine(enumerateDirectoryName, "Program.cs"),
                Path.Combine(enumerateDirectoryName, "Setup.iss")
            };

            string enumerateDirectoryPath = this.Enlistment.GetVirtualPathTo(enumerateDirectoryName);
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

            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

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
        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void PersistedDirectoryTests(FileSystemRunner fileSystem)
        {
            // Delete File Setup
            string deleteFileName = ".gitattributes";
            string deleteFilepath = this.Enlistment.GetVirtualPathTo(deleteFileName);
            fileSystem.DeleteFile(deleteFilepath);

            // Delete Folder Setup
            string deleteFolderName = Path.Combine("GVFS", "GVFS");
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
            // Write a file to GVFS to ensure it has a physical folder
            string childFileToAdd = Path.Combine("GVFS", "ChildFileToAdd.txt");
            string childFileToAddContent = "This is new child file in the GVFS folder.";
            string childFileToAddPath = this.Enlistment.GetVirtualPathTo(childFileToAdd);
            fileSystem.WriteAllText(childFileToAddPath, childFileToAddContent);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

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
            string childFolder = Path.Combine("GVFS", "GVFS.FunctionalTests");
            string childFolderPath = this.Enlistment.GetVirtualPathTo(childFolder);
            childFolderPath.ShouldBeADirectory(fileSystem);
            string postMountChildFile = "PostMountChildFile.txt";
            string postMountChildFileContent = "This is new child file added after the mount";
            string postMountChildFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(childFolder, postMountChildFile));
            fileSystem.WriteAllText(postMountChildFilePath, postMountChildFileContent); // Verify we can create files in subfolders of GVFS
            postMountChildFilePath.ShouldBeAFile(fileSystem).WithContents().ShouldEqual(postMountChildFileContent);

            // 663045 - Ensure that folder can be deleted after a new file is added and GVFS is remounted
            fileSystem.DeleteDirectory(childFolderPath);
            childFolderPath.ShouldNotExistOnDisk(fileSystem);
        }
    }
}