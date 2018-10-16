using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
    public class GitFilesTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        public GitFilesTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void CreateFileTest()
        {
            string fileName = "file1.txt";
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileName);
            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(fileName), "Some content here");
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileName);
            this.Enlistment.GetVirtualPathTo(fileName).ShouldBeAFile(this.fileSystem).WithContents("Some content here");

            string emptyFileName = "file1empty.txt";
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, emptyFileName);
            this.fileSystem.CreateEmptyFile(this.Enlistment.GetVirtualPathTo(emptyFileName));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, emptyFileName);
            this.Enlistment.GetVirtualPathTo(emptyFileName).ShouldBeAFile(this.fileSystem);
        }

        [TestCase, Order(2)]
        public void CreateHardLinkTest()
        {
            string existingFileName = "fileToLinkTo.txt";
            string existingFilePath = this.Enlistment.GetVirtualPathTo(existingFileName);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, existingFileName);
            this.fileSystem.WriteAllText(existingFilePath, "Some content here");
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, existingFileName);
            existingFilePath.ShouldBeAFile(this.fileSystem).WithContents("Some content here");

            string newLinkFileName = "newHardLink.txt";
            string newLinkFilePath = this.Enlistment.GetVirtualPathTo(newLinkFileName);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, newLinkFileName);
            this.fileSystem.CreateHardLink(newLinkFilePath, existingFilePath);
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, newLinkFileName);
            newLinkFilePath.ShouldBeAFile(this.fileSystem).WithContents("Some content here");
        }

        [TestCase, Order(3)]
        [Category(Categories.MacTODO.M2)]
        public void CreateFileInFolderTest()
        {
            string folderName = "folder2";
            string fileName = "file2.txt";
            string filePath = Path.Combine(folderName, fileName);

            this.Enlistment.GetVirtualPathTo(filePath).ShouldNotExistOnDisk(this.fileSystem);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, filePath);

            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(folderName));
            this.fileSystem.CreateEmptyFile(this.Enlistment.GetVirtualPathTo(filePath));
            this.Enlistment.GetVirtualPathTo(filePath).ShouldBeAFile(this.fileSystem);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, folderName + "/");
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, folderName + "/" + fileName);
        }

        [TestCase, Order(4)]
        [Category(Categories.MacTODO.M3)]
        public void RenameEmptyFolderTest()
        {
            string folderName = "folder3a";
            string renamedFolderName = "folder3b";
            string[] expectedModifiedEntries =
            {
                renamedFolderName + "/",
            };

            this.Enlistment.GetVirtualPathTo(folderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(folderName));
            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(folderName), this.Enlistment.GetVirtualPathTo(renamedFolderName));

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, expectedModifiedEntries);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, folderName + "/");
        }

        [TestCase, Order(5)]
        [Category(Categories.MacTODO.M2)]
        public void RenameFolderTest()
        {
            string folderName = "folder4a";
            string renamedFolderName = "folder4b";
            string[] fileNames = { "a", "b", "c" };
            string[] expectedModifiedEntries =
            {
                renamedFolderName + "/",
            };

            string[] unexpectedModifiedEntries =
            {
                renamedFolderName + "/" + fileNames[0],
                renamedFolderName + "/" + fileNames[1],
                renamedFolderName + "/" + fileNames[2],
                folderName + "/",
                folderName + "/" + fileNames[0],
                folderName + "/" + fileNames[1],
                folderName + "/" + fileNames[2],
            };

            this.Enlistment.GetVirtualPathTo(folderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(folderName));
            foreach (string fileName in fileNames)
            {
                string filePath = Path.Combine(folderName, fileName);
                this.fileSystem.CreateEmptyFile(this.Enlistment.GetVirtualPathTo(filePath));
                this.Enlistment.GetVirtualPathTo(filePath).ShouldBeAFile(this.fileSystem);
            }

            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(folderName), this.Enlistment.GetVirtualPathTo(renamedFolderName));

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, expectedModifiedEntries);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, unexpectedModifiedEntries);
        }

        [TestCase, Order(6)]
        [Category(Categories.MacTODO.M2)]
        public void CaseOnlyRenameOfNewFolderKeepsModifiedPathsEntries()
        {
            if (this.fileSystem is PowerShellRunner)
            {
                Assert.Ignore("Powershell does not support case only renames.");
            }

            this.fileSystem.CreateDirectory(Path.Combine(this.Enlistment.RepoRoot, "Folder"));
            this.fileSystem.CreateEmptyFile(Path.Combine(this.Enlistment.RepoRoot, "Folder", "testfile"));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, "Folder/");
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, "Folder/testfile");

            this.fileSystem.RenameDirectory(this.Enlistment.RepoRoot, "Folder", "folder");
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, "folder/");
            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, "folder/testfile");
        }

        [TestCase, Order(7)]
        public void ReadingFileDoesNotUpdateIndexOrModifiedPaths()
        {
            string gitFileToCheck = "GVFS/GVFS.FunctionalTests/Category/CategoryConstants.cs";
            string virtualFile = this.Enlistment.GetVirtualPathTo(gitFileToCheck);
            ProcessResult initialResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files --debug -svmodc " + gitFileToCheck);
            initialResult.ShouldNotBeNull();
            initialResult.Output.ShouldNotBeNull();
            initialResult.Output.StartsWith("S ").ShouldEqual(true);
            initialResult.Output.ShouldContain("ctime: 0:0", "mtime: 0:0", "size: 0\t");

            using (FileStream fileStreamToRead = File.OpenRead(virtualFile))
            {
                fileStreamToRead.ReadByte();
            }

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations did not complete.");

            ProcessResult afterUpdateResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files --debug -svmodc " + gitFileToCheck);
            afterUpdateResult.ShouldNotBeNull();
            afterUpdateResult.Output.ShouldNotBeNull();
            afterUpdateResult.Output.StartsWith("S ").ShouldEqual(true);
            afterUpdateResult.Output.ShouldContain("ctime: 0:0", "mtime: 0:0", "size: 0\t");

            GVFSHelpers.ModifiedPathsShouldNotContain(this.fileSystem, this.Enlistment.DotGVFSRoot, gitFileToCheck);
        }

        [TestCase, Order(8)]
        [Category(Categories.MacTODO.NeedsLockHolder)]
        public void ModifiedFileWillGetAddedToModifiedPathsFile()
        {
            string gitFileToTest = "GVFS/GVFS.Common/RetryWrapper.cs";
            string fileToCreate = this.Enlistment.GetVirtualPathTo(gitFileToTest);
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.SkipWorktree);

            ManualResetEventSlim resetEvent = GitHelpers.AcquireGVFSLock(this.Enlistment, out _);

            this.fileSystem.WriteAllText(fileToCreate, "Anything can go here");
            this.fileSystem.FileExists(fileToCreate).ShouldEqual(true);
            resetEvent.Set();

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations did not complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, gitFileToTest);
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.Cached);
        }

        [TestCase, Order(9)]
        public void RenamedFileAddedToModifiedPathsFile()
        {
            string fileToRenameEntry = "Test_EPF_MoveRenameFileTests/ChangeUnhydratedFileName/Program.cs";
            string fileToRenameTargetEntry = "Test_EPF_MoveRenameFileTests/ChangeUnhydratedFileName/Program2.cs";
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.SkipWorktree);

            this.fileSystem.MoveFile(
                this.Enlistment.GetVirtualPathTo(fileToRenameEntry), 
                this.Enlistment.GetVirtualPathTo(fileToRenameTargetEntry));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToRenameEntry);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToRenameTargetEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.Cached);
        }

        [TestCase, Order(10)]
        public void RenamedFileAndOverwrittenTargetAddedToModifiedPathsFile()
        {
            string fileToRenameEntry = "Test_EPF_MoveRenameFileTests_2/MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite/RunUnitTests.bat";
            string fileToRenameTargetEntry = "Test_EPF_MoveRenameFileTests_2/MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite/RunFunctionalTests.bat";
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.SkipWorktree);
            this.VerifyWorktreeBit(fileToRenameTargetEntry, LsFilesStatus.SkipWorktree);

            this.fileSystem.ReplaceFile(
                this.Enlistment.GetVirtualPathTo(fileToRenameEntry),
                this.Enlistment.GetVirtualPathTo(fileToRenameTargetEntry));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToRenameEntry);
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToRenameTargetEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.Cached);
            this.VerifyWorktreeBit(fileToRenameTargetEntry, LsFilesStatus.Cached);
        }

        [TestCase, Order(11)]
        public void DeletedFileAddedToModifiedPathsFile()
        {
            string fileToDeleteEntry = "GVFlt_DeleteFileTest/GVFlt_DeleteFullFileWithoutFileContext_DeleteOnClose/a.txt";
            this.VerifyWorktreeBit(fileToDeleteEntry, LsFilesStatus.SkipWorktree);

            this.fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(fileToDeleteEntry));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToDeleteEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToDeleteEntry, LsFilesStatus.Cached);
        }

        [TestCase, Order(12)]
        public void DeletedFolderAndChildrenAddedToToModifiedPathsFile()
        {
            string folderToDelete = "Scripts";
            string[] filesToDelete = new string[]
            {
                "Scripts/CreateCommonAssemblyVersion.bat",
                "Scripts/CreateCommonCliAssemblyVersion.bat",
                "Scripts/CreateCommonVersionHeader.bat",
                "Scripts/RunFunctionalTests.bat",
                "Scripts/RunUnitTests.bat"
            };

            // Verify skip-worktree initial set for all files
            foreach (string file in filesToDelete)
            {
                this.VerifyWorktreeBit(file, LsFilesStatus.SkipWorktree);
            }

            this.fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(folderToDelete));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, folderToDelete + "/");
            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, filesToDelete);

            // Verify skip-worktree cleared
            foreach (string file in filesToDelete)
            {
                this.VerifyWorktreeBit(file, LsFilesStatus.Cached);
            }
        }

        [TestCase, Order(13)]
        public void FileRenamedOutOfRepoAddedToModifiedPathsAndSkipWorktreeBitCleared()
        {
            string fileToRenameEntry = "GVFlt_MoveFileTest/PartialToOutside/from/lessInFrom.txt";
            string fileToRenameVirtualPath = this.Enlistment.GetVirtualPathTo(fileToRenameEntry);
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.SkipWorktree);

            string fileOutsideRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, $"{nameof(this.FileRenamedOutOfRepoAddedToModifiedPathsAndSkipWorktreeBitCleared)}.txt");
            this.fileSystem.MoveFile(fileToRenameVirtualPath, fileOutsideRepoPath);
            fileOutsideRepoPath.ShouldBeAFile(this.fileSystem).WithContents("lessData");

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToRenameEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameEntry, LsFilesStatus.Cached);
        }

        [TestCase, Order(14)]
        public void OverwrittenFileAddedToModifiedPathsAndSkipWorktreeBitCleared()
        {
            string fileToOverwriteEntry = "Test_EPF_WorkingDirectoryTests/1/2/3/4/ReadDeepProjectedFile.cpp";
            string fileToOverwriteVirtualPath = this.Enlistment.GetVirtualPathTo(fileToOverwriteEntry);
            this.VerifyWorktreeBit(fileToOverwriteEntry, LsFilesStatus.SkipWorktree);

            string testContents = $"Test contents for {nameof(this.OverwrittenFileAddedToModifiedPathsAndSkipWorktreeBitCleared)}";

            this.fileSystem.WriteAllText(fileToOverwriteVirtualPath, testContents);
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            fileToOverwriteVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(testContents);

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToOverwriteEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToOverwriteEntry, LsFilesStatus.Cached);
        }

        [TestCase, Order(15)]
        [Category(Categories.MacTODO.M2)]
        public void SupersededFileAddedToModifiedPathsAndSkipWorktreeBitCleared()
        {
            string fileToSupersedeEntry = "GVFlt_FileOperationTest/WriteAndVerify.txt";
            string fileToSupersedePath = this.Enlistment.GetVirtualPathTo("GVFlt_FileOperationTest\\WriteAndVerify.txt");
            this.VerifyWorktreeBit(fileToSupersedeEntry, LsFilesStatus.SkipWorktree);

            string newContent = $"{nameof(this.SupersededFileAddedToModifiedPathsAndSkipWorktreeBitCleared)} test new contents";

            SupersedeFile(fileToSupersedePath, newContent).ShouldEqual(true);
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            GVFSHelpers.ModifiedPathsShouldContain(this.fileSystem, this.Enlistment.DotGVFSRoot, fileToSupersedeEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToSupersedeEntry, LsFilesStatus.Cached);

            // Verify new content written
            fileToSupersedePath.ShouldBeAFile(this.fileSystem).WithContents(newContent);
        }

        [DllImport("GVFS.NativeTests.dll", CharSet = CharSet.Unicode)]
        private static extern bool SupersedeFile(string path, [MarshalAs(UnmanagedType.LPStr)]string newContent);

        private void VerifyWorktreeBit(string path, char expectedStatus)
        {
            ProcessResult lsfilesResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files -svomdc " + path);
            lsfilesResult.ShouldNotBeNull();
            lsfilesResult.Output.ShouldNotBeNull();
            lsfilesResult.Output.Length.ShouldBeAtLeast(2);
            lsfilesResult.Output[0].ShouldEqual(expectedStatus);
        }

        private static class LsFilesStatus
        {
            public const char Cached = 'H';
            public const char SkipWorktree = 'S';
        }
    }
}
