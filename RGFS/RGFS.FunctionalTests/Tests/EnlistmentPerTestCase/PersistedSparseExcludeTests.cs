using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class PersistedSparseExcludeTests : TestsWithEnlistmentPerTestCase
    {
        private const string FileToAdd = @"RGFS\TestAddFile.txt";
        private const string FileToUpdate = @"RGFS\RGFS\Program.cs";
        private const string FileToDelete = "Readme.md";
        private const string FileToRename = @"RGFS\RGFS.Mount\MountVerb.cs";
        private const string RenameFileTarget = @"RGFS\RGFS.Mount\MountVerb2.cs";
        private const string FolderToCreate = "PersistedSparseExcludeTests_NewFolder";
        private const string FolderToRename = "PersistedSparseExcludeTests_NewFolderForRename";
        private const string RenameFolderTarget = "PersistedSparseExcludeTests_NewFolderForRename2";
        private const string DotGitFileToCreate = @".git\TestFileFromDotGit.txt";
        private const string RenameNewDotGitFileTarget = "TestFileFromDotGit.txt";
        private const string FileToCreateOutsideRepo = "PersistedSparseExcludeTests_outsideRepo.txt";
        private const string FolderToCreateOutsideRepo = "PersistedSparseExcludeTests_outsideFolder";
        private const string FolderToDelete = "Scripts";
        private const string AlwaysExcludeFilePath = @".git\info\always_exclude";
        private const string SparseCheckoutFilePath = @".git\info\sparse-checkout";
        private static string[] expectedAlwaysExcludeFileContents = new string[] 
        {
            "*",
            "!/RGFS/",
            "!/RGFS/TestAddFile.txt",
            "!/RGFS/RGFS.Mount/",
            "!/RGFS/RGFS.Mount/MountVerb2.cs",
            "!/TestFileFromDotGit.txt",
            "!/PersistedSparseExcludeTests_outsideRepo.txt"
        };
        private static string[] expectedSparseFileContents = new string[] 
        {
            "/.gitattributes",
            "/RGFS/RGFS/Program.cs",
            "/RGFS/TestAddFile.txt",
            "/Readme.md",
            "/RGFS/RGFS.Mount/MountVerb.cs",
            "/RGFS/RGFS.Mount/MountVerb2.cs",
            "/PersistedSparseExcludeTests_NewFolder/",
            "/PersistedSparseExcludeTests_NewFolderForRename/",
            "/PersistedSparseExcludeTests_NewFolderForRename2/",
            "/TestFileFromDotGit.txt",
            "/PersistedSparseExcludeTests_outsideRepo.txt",
            "/PersistedSparseExcludeTests_outsideFolder/",
            "/Scripts/CreateCommonAssemblyVersion.bat",
            "/Scripts/CreateCommonCliAssemblyVersion.bat",
            "/Scripts/CreateCommonVersionHeader.bat",
            "/Scripts/RunFunctionalTests.bat",
            "/Scripts/RunUnitTests.bat",
            "/Scripts/"
        };

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void ExcludeSparseFileSavedAfterRemount(FileSystemRunner fileSystem)
        {
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

            // Moving the new folder out of the repo should not change the always_exclude file
            string folderTargetOutsideSrc = Path.Combine(this.Enlistment.EnlistmentRoot, RenameFolderTarget);
            folderTargetOutsideSrc.ShouldNotExistOnDisk(fileSystem);
            fileSystem.MoveDirectory(folderToRenameTarget, folderTargetOutsideSrc);
            folderTargetOutsideSrc.ShouldBeADirectory(fileSystem);
            folderToRenameTarget.ShouldNotExistOnDisk(fileSystem);

            // Moving a file from the .git folder to the working directory should add the file to the sparse-checkout
            string dotGitfileToAdd = this.Enlistment.GetVirtualPathTo(DotGitFileToCreate);
            fileSystem.WriteAllText(dotGitfileToAdd, "Contents for the new file in dot git");
            fileSystem.MoveFile(dotGitfileToAdd, this.Enlistment.GetVirtualPathTo(RenameNewDotGitFileTarget));

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

            string folderToDelete = this.Enlistment.GetVirtualPathTo(FolderToDelete);
            fileSystem.DeleteDirectory(folderToDelete);
            folderToDelete.ShouldNotExistOnDisk(fileSystem);

            // Remount
            this.Enlistment.UnmountRGFS();
            this.Enlistment.MountRGFS();

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            string alwaysExcludeFile = this.Enlistment.GetVirtualPathTo(AlwaysExcludeFilePath);
            string alwaysExcludeFileContents = alwaysExcludeFile.ShouldBeAFile(fileSystem).WithContents();

            alwaysExcludeFileContents.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !x.StartsWith("#")) // Exclude comments
                .OrderBy(x => x)
                .ShouldMatchInOrder(expectedAlwaysExcludeFileContents.OrderBy(x => x));

            string sparseFile = this.Enlistment.GetVirtualPathTo(SparseCheckoutFilePath);
            string sparseFileContents = sparseFile.ShouldBeAFile(fileSystem).WithContents();
            sparseFileContents.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(x => x)
                .ShouldMatchInOrder(expectedSparseFileContents.OrderBy(x => x));
        }      
    }
}
