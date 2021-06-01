using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class ModifiedPathsTests : TestsWithEnlistmentPerTestCase
    {
        private static readonly string FileToAdd = Path.Combine("GVFS", "TestAddFile.txt");
        private static readonly string FileToUpdate = Path.Combine("GVFS", "GVFS", "Program.cs");
        private static readonly string FileToDelete = "Readme.md";
        private static readonly string FileToRename = Path.Combine("GVFS", "GVFS.Mount", "MountVerb.cs");
        private static readonly string RenameFileTarget = Path.Combine("GVFS", "GVFS.Mount", "MountVerb2.cs");
        private static readonly string FolderToCreate = $"{nameof(ModifiedPathsTests)}_NewFolder";
        private static readonly string FolderToRename = $"{nameof(ModifiedPathsTests)}_NewFolderForRename";
        private static readonly string RenameFolderTarget = $"{nameof(ModifiedPathsTests)}_NewFolderForRename2";
        private static readonly string DotGitFileToCreate = Path.Combine(".git", "TestFileFromDotGit.txt");
        private static readonly string RenameNewDotGitFileTarget = "TestFileFromDotGit.txt";
        private static readonly string FileToCreateOutsideRepo = $"{nameof(ModifiedPathsTests)}_outsideRepo.txt";
        private static readonly string FolderToCreateOutsideRepo = $"{nameof(ModifiedPathsTests)}_outsideFolder";
        private static readonly string FolderToDelete = "Scripts";

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void DeletedTempFileIsRemovedFromModifiedFiles(FileSystemRunner fileSystem)
        {
            string tempFile = this.CreateFile(fileSystem, "temp.txt");
            fileSystem.DeleteFile(tempFile);
            tempFile.ShouldNotExistOnDisk(fileSystem);

            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, fileSystem, "temp.txt");
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void DeletedTempFolderIsRemovedFromModifiedFiles(FileSystemRunner fileSystem)
        {
            string tempFolder = this.CreateDirectory(fileSystem, "Temp");
            fileSystem.DeleteDirectory(tempFolder);
            tempFolder.ShouldNotExistOnDisk(fileSystem);

            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, fileSystem, "Temp/");
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void DeletedTempFolderDeletesFilesFromModifiedFiles(FileSystemRunner fileSystem)
        {
            string tempFolder = this.CreateDirectory(fileSystem, "Temp");
            string tempFile1 = this.CreateFile(fileSystem, Path.Combine("Temp", "temp1.txt"));
            string tempFile2 = this.CreateFile(fileSystem, Path.Combine("Temp", "temp2.txt"));
            fileSystem.DeleteDirectory(tempFolder);
            tempFolder.ShouldNotExistOnDisk(fileSystem);
            tempFile1.ShouldNotExistOnDisk(fileSystem);
            tempFile2.ShouldNotExistOnDisk(fileSystem);

            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, fileSystem, "Temp/", "Temp/temp1.txt", "Temp/temp2.txt");
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        [Category(Categories.MacTODO.NeedsNewFolderCreateNotification)]
        public void ModifiedPathsFromChangesInsideRepoSavedAfterRemount(FileSystemRunner fileSystem)
        {
            string[] expectedModifiedFilesContentsAfterRemount =
                {
                    @"A .gitattributes",
                    $"A {GVFSHelpers.ConvertPathToGitFormat(FileToAdd)}",
                    $"A {GVFSHelpers.ConvertPathToGitFormat(FileToUpdate)}",
                    $"A {FileToDelete}",
                    $"A {GVFSHelpers.ConvertPathToGitFormat(FileToRename)}",
                    $"A {GVFSHelpers.ConvertPathToGitFormat(RenameFileTarget)}",
                    $"A {FolderToCreate}/",
                    $"A {RenameNewDotGitFileTarget}",
                    $"A {FolderToDelete}/",
                };

            string fileToAdd = this.Enlistment.GetVirtualPathTo(FileToAdd);
            fileSystem.WriteAllText(fileToAdd, "Contents for the new file");

            string fileToUpdate = this.Enlistment.GetVirtualPathTo(FileToUpdate);
            fileSystem.AppendAllText(fileToUpdate, "// Testing");

            string fileToDelete = this.Enlistment.GetVirtualPathTo(FileToDelete);
            fileSystem.DeleteFile(fileToDelete);
            fileToDelete.ShouldNotExistOnDisk(fileSystem);

            string fileToRename = this.Enlistment.GetVirtualPathTo(FileToRename);
            fileSystem.MoveFile(fileToRename, this.Enlistment.GetVirtualPathTo(RenameFileTarget));

            string folderToCreate = this.Enlistment.GetVirtualPathTo(FolderToCreate);
            fileSystem.CreateDirectory(folderToCreate);

            string folderToRename = this.Enlistment.GetVirtualPathTo(FolderToRename);
            fileSystem.CreateDirectory(folderToRename);
            string folderToRenameTarget = this.Enlistment.GetVirtualPathTo(RenameFolderTarget);
            fileSystem.MoveDirectory(folderToRename, folderToRenameTarget);

            // Deleting the new folder will remove it from the modified paths file
            fileSystem.DeleteDirectory(folderToRenameTarget);
            folderToRenameTarget.ShouldNotExistOnDisk(fileSystem);

            // Moving a file from the .git folder to the working directory should add the file to the modified paths
            string dotGitfileToAdd = this.Enlistment.GetVirtualPathTo(DotGitFileToCreate);
            fileSystem.WriteAllText(dotGitfileToAdd, "Contents for the new file in dot git");
            fileSystem.MoveFile(dotGitfileToAdd, this.Enlistment.GetVirtualPathTo(RenameNewDotGitFileTarget));

            string folderToDeleteFullPath = this.Enlistment.GetVirtualPathTo(FolderToDelete);
            fileSystem.WriteAllText(Path.Combine(folderToDeleteFullPath, "NewFile.txt"), "Contents for new file");
            string newFileToDelete = Path.Combine(folderToDeleteFullPath, "NewFileToDelete.txt");
            fileSystem.WriteAllText(newFileToDelete, "Contents for new file");
            fileSystem.DeleteFile(newFileToDelete);
            fileSystem.WriteAllText(Path.Combine(folderToDeleteFullPath, "CreateCommonVersionHeader.bat"), "Changing the file contents");
            fileSystem.DeleteFile(Path.Combine(folderToDeleteFullPath, "RunUnitTests.bat"));

            fileSystem.DeleteDirectory(folderToDeleteFullPath);
            folderToDeleteFullPath.ShouldNotExistOnDisk(fileSystem);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            this.Enlistment.WaitForBackgroundOperations();

            string modifiedPathsDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            using (StreamReader reader = new StreamReader(File.Open(modifiedPathsDatabase, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.ReadToEnd().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x)
                    .ShouldMatchInOrder(expectedModifiedFilesContentsAfterRemount.OrderBy(x => x));
            }
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        [Category(Categories.RepositoryMountsSameFileSystem)]
        [Category(Categories.MacTODO.NeedsNewFolderCreateNotification)]
        public void ModifiedPathsFromRenamingOutsideRepoSavedAfterRemount(FileSystemRunner fileSystem)
        {
            string[] expectedModifiedFilesContentsAfterRemount =
                {
                    @"A .gitattributes",
                    $"A {FileToCreateOutsideRepo}",
                    $"A {FolderToCreateOutsideRepo}/",
                };

            string folderToRename = this.Enlistment.GetVirtualPathTo(FolderToRename);
            fileSystem.CreateDirectory(folderToRename);
            string folderToRenameTarget = this.Enlistment.GetVirtualPathTo(RenameFolderTarget);
            fileSystem.MoveDirectory(folderToRename, folderToRenameTarget);

            // Moving the new folder out of the repo will remove it from the modified paths file
            string folderTargetOutsideSrc = Path.Combine(this.Enlistment.EnlistmentRoot, RenameFolderTarget);
            folderTargetOutsideSrc.ShouldNotExistOnDisk(fileSystem);
            fileSystem.MoveDirectory(folderToRenameTarget, folderTargetOutsideSrc);
            folderTargetOutsideSrc.ShouldBeADirectory(fileSystem);
            folderToRenameTarget.ShouldNotExistOnDisk(fileSystem);

            // Move a file from outside of src into src
            string fileToCreateOutsideRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, FileToCreateOutsideRepo);
            fileSystem.WriteAllText(fileToCreateOutsideRepoPath, "Contents for the new file outside of repo");
            string fileToCreateOutsideRepoTargetPath = this.Enlistment.GetVirtualPathTo(FileToCreateOutsideRepo);
            fileToCreateOutsideRepoTargetPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.MoveFile(fileToCreateOutsideRepoPath, fileToCreateOutsideRepoTargetPath);
            fileToCreateOutsideRepoTargetPath.ShouldBeAFile(fileSystem);
            fileToCreateOutsideRepoPath.ShouldNotExistOnDisk(fileSystem);

            // Move a folder from outside of src into src
            string folderToCreateOutsideRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, FolderToCreateOutsideRepo);
            fileSystem.CreateDirectory(folderToCreateOutsideRepoPath);
            folderToCreateOutsideRepoPath.ShouldBeADirectory(fileSystem);
            string folderToCreateOutsideRepoTargetPath = this.Enlistment.GetVirtualPathTo(FolderToCreateOutsideRepo);
            folderToCreateOutsideRepoTargetPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.MoveDirectory(folderToCreateOutsideRepoPath, folderToCreateOutsideRepoTargetPath);
            folderToCreateOutsideRepoTargetPath.ShouldBeADirectory(fileSystem);
            folderToCreateOutsideRepoPath.ShouldNotExistOnDisk(fileSystem);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            this.Enlistment.WaitForBackgroundOperations();

            string modifiedPathsDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            using (StreamReader reader = new StreamReader(File.Open(modifiedPathsDatabase, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.ReadToEnd().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x)
                    .ShouldMatchInOrder(expectedModifiedFilesContentsAfterRemount.OrderBy(x => x));
            }
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        public void ModifiedPathsCorrectAfterHardLinkingInsideRepo(FileSystemRunner fileSystem)
        {
            string[] expectedModifiedFilesContentsAfterHardlinks =
                {
                    "A .gitattributes",
                    "A LinkToReadme.md",
                    "A Readme.md",
                };

            // Create a link from src\LinkToReadme.md to src\Readme.md
            string existingFileInRepoPath = this.Enlistment.GetVirtualPathTo("Readme.md");
            string contents = existingFileInRepoPath.ShouldBeAFile(fileSystem).WithContents();
            string hardLinkToFileInRepoPath = this.Enlistment.GetVirtualPathTo("LinkToReadme.md");
            hardLinkToFileInRepoPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateHardLink(hardLinkToFileInRepoPath, existingFileInRepoPath);
            hardLinkToFileInRepoPath.ShouldBeAFile(fileSystem).WithContents(contents);

            this.Enlistment.WaitForBackgroundOperations();

            string modifiedPathsDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            using (StreamReader reader = new StreamReader(File.Open(modifiedPathsDatabase, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.ReadToEnd().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x)
                    .ShouldMatchInOrder(expectedModifiedFilesContentsAfterHardlinks.OrderBy(x => x));
            }
        }

        [TestCaseSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
        [Category(Categories.RepositoryMountsSameFileSystem)]
        public void ModifiedPathsCorrectAfterHardLinkingOutsideRepo(FileSystemRunner fileSystem)
        {
            string[] expectedModifiedFilesContentsAfterHardlinks =
                {
                    "A .gitattributes",
                    "A LinkToFileOutsideSrc.txt",
                    "A GVFS/GVFS/Program.cs",
                };

            // Create a link from src\LinkToFileOutsideSrc.txt to FileOutsideRepo.txt
            string fileOutsideOfRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, "FileOutsideRepo.txt");
            string fileOutsideOfRepoContents = "File outside of repo";
            fileOutsideOfRepoPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.WriteAllText(fileOutsideOfRepoPath, fileOutsideOfRepoContents);
            string hardLinkToFileOutsideRepoPath = this.Enlistment.GetVirtualPathTo("LinkToFileOutsideSrc.txt");
            hardLinkToFileOutsideRepoPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateHardLink(hardLinkToFileOutsideRepoPath, fileOutsideOfRepoPath);
            hardLinkToFileOutsideRepoPath.ShouldBeAFile(fileSystem).WithContents(fileOutsideOfRepoContents);

            // Create a link from LinkOutsideSrcToInsideSrc.cs to src\GVFS\GVFS\Program.cs
            string secondFileInRepoPath = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS", "Program.cs");
            string contents = secondFileInRepoPath.ShouldBeAFile(fileSystem).WithContents();
            string hardLinkOutsideRepoToFileInRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, "LinkOutsideSrcToInsideSrc.cs");
            hardLinkOutsideRepoToFileInRepoPath.ShouldNotExistOnDisk(fileSystem);
            fileSystem.CreateHardLink(hardLinkOutsideRepoToFileInRepoPath, secondFileInRepoPath);
            hardLinkOutsideRepoToFileInRepoPath.ShouldBeAFile(fileSystem).WithContents(contents);

            this.Enlistment.WaitForBackgroundOperations();

            string modifiedPathsDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            using (StreamReader reader = new StreamReader(File.Open(modifiedPathsDatabase, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.ReadToEnd().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x)
                    .ShouldMatchInOrder(expectedModifiedFilesContentsAfterHardlinks.OrderBy(x => x));
            }
        }

        private string CreateDirectory(FileSystemRunner fileSystem, string relativePath)
        {
            string tempFolder = this.Enlistment.GetVirtualPathTo(relativePath);
            fileSystem.CreateDirectory(tempFolder);
            tempFolder.ShouldBeADirectory(fileSystem);
            return tempFolder;
        }

        private string CreateFile(FileSystemRunner fileSystem, string relativePath)
        {
            string tempFile = this.Enlistment.GetVirtualPathTo(relativePath);
            fileSystem.WriteAllText(tempFile, $"Contents for the {relativePath} file");
            tempFile.ShouldBeAFile(fileSystem);
            return tempFile;
        }
    }
}
