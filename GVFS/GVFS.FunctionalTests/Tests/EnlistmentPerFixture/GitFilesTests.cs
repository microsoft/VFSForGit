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
    [TestFixtureSource(typeof(GitFilesTestsRunners), GitFilesTestsRunners.TestRunners)]
    public class GitFilesTests : TestsWithEnlistmentPerFixture
    {
        private const string AlwaysExcludeFileContentsBeforeChange = "*";
        private const string AlwaysExcludeFileContentsAfterChange =
@"*
!/*";

        private FileSystemRunner fileSystem;

        public GitFilesTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void CreateFileTest()
        {
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, "tempFile.txt");
            string alwaysExcludeFile = Path.Combine(this.Enlistment.RepoRoot, GitHelpers.AlwaysExcludeFilePath);
            alwaysExcludeFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(AlwaysExcludeFileContentsBeforeChange.Replace("\r\n", "\n"));
            this.fileSystem.WriteAllText(virtualFile, "Some content here");

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            virtualFile.ShouldBeAFile(this.fileSystem).WithContents("Some content here");
            alwaysExcludeFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(AlwaysExcludeFileContentsAfterChange.Replace("\r\n", "\n"));
        }

        [TestCase, Order(2)]
        public void ReadingFileDoesNotUpdateIndexOrSparseCheckout()
        {
            string gitFileToCheck = "GVFS/GVFS.FunctionalTests/Category/CategoryConstants.cs";
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, gitFileToCheck.Replace('/', '\\'));
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

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldNotContain(ignoreCase: true, unexpectedSubstrings: gitFileToCheck);
        }

        [TestCase, Order(3)]
        public void ModifiedFileWillGetSkipworktreeBitCleared()
        {
            string fileToTest = "GVFS\\GVFS.Common\\RetryWrapper.cs";
            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, fileToTest);
            string gitFileToTest = fileToTest.Replace('\\', '/');
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.SkipWorktree);

            ManualResetEventSlim resetEvent = GitHelpers.AcquireGVFSLock(this.Enlistment);

            this.fileSystem.WriteAllText(fileToCreate, "Anything can go here");
            this.fileSystem.FileExists(fileToCreate).ShouldEqual(true);
            resetEvent.Set();

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations did not complete.");

            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(gitFileToTest);
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.Cached);
        }

        [TestCase, Order(4)]
        public void RenamedFileAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToRenameSparseCheckoutEntry = "/Test_EPF_MoveRenameFileTests/ChangeUnhydratedFileName/Program.cs";
            string fileToRenameTargetSparseCheckoutEntry = "/Test_EPF_MoveRenameFileTests/ChangeUnhydratedFileName/Program2.cs";
            string fileToRenameRelativePath = "Test_EPF_MoveRenameFileTests\\ChangeUnhydratedFileName\\Program.cs";
            string fileToRenameTargetRelativePath = "Test_EPF_MoveRenameFileTests\\ChangeUnhydratedFileName\\Program2.cs";
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            this.fileSystem.MoveFile(
                this.Enlistment.GetVirtualPathTo(fileToRenameRelativePath), 
                this.Enlistment.GetVirtualPathTo(fileToRenameTargetRelativePath));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToRenameSparseCheckoutEntry);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToRenameTargetSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
        }

        [TestCase, Order(5)]
        public void RenamedFileAndOverwrittenTargetAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToRenameSparseCheckoutEntry = "/Test_EPF_MoveRenameFileTests_2/MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite/RunUnitTests.bat";
            string fileToRenameTargetSparseCheckoutEntry = "/Test_EPF_MoveRenameFileTests_2/MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite/RunFunctionalTests.bat";
            string fileToRenameRelativePath = "Test_EPF_MoveRenameFileTests_2\\MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite\\RunUnitTests.bat";
            string fileToRenameTargetRelativePath = "Test_EPF_MoveRenameFileTests_2\\MoveUnhydratedFileToOverwriteUnhydratedFileAndWrite\\RunFunctionalTests.bat";
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);
            this.VerifyWorktreeBit(fileToRenameTargetSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            this.fileSystem.ReplaceFile(
                this.Enlistment.GetVirtualPathTo(fileToRenameRelativePath),
                this.Enlistment.GetVirtualPathTo(fileToRenameTargetRelativePath));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToRenameSparseCheckoutEntry);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToRenameTargetSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
            this.VerifyWorktreeBit(fileToRenameTargetSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
        }

        [TestCase, Order(6)]
        public void DeletedFileAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToDeleteSparseCheckoutEntry = "/GVFlt_DeleteFileTest/GVFlt_DeleteFullFileWithoutFileContext_DeleteOnClose/a.txt";
            string fileToDeleteRelativePath = "GVFlt_DeleteFileTest\\GVFlt_DeleteFullFileWithoutFileContext_DeleteOnClose\\a.txt";
            this.VerifyWorktreeBit(fileToDeleteSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            this.fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(fileToDeleteRelativePath));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToDeleteSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToDeleteSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
        }

        [TestCase, Order(7)]
        public void DeletedFolderAndChildrenAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string folderToDelete = "Scripts";
            string[] filesToDelete = new string[]
            {
                "/Scripts/CreateCommonAssemblyVersion.bat",
                "/Scripts/CreateCommonCliAssemblyVersion.bat",
                "/Scripts/CreateCommonVersionHeader.bat",
                "/Scripts/RunFunctionalTests.bat",
                "/Scripts/RunUnitTests.bat"
            };

            // Verify skip-worktree initial set for all files
            foreach (string file in filesToDelete)
            {
                this.VerifyWorktreeBit(file.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);
            }

            this.fileSystem.DeleteDirectory(this.Enlistment.GetVirtualPathTo(folderToDelete));
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain('/' + folderToDelete + '/');
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(filesToDelete);

            // Verify skip-worktree cleared
            foreach (string file in filesToDelete)
            {
                this.VerifyWorktreeBit(file.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
            }
        }

        [TestCase, Order(8)]
        public void FileRenamedOutOfRepoAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToRenameSparseCheckoutEntry = "/GVFlt_MoveFileTest/PartialToOutside/from/lessInFrom.txt";
            string fileToRenameVirtualPath = this.Enlistment.GetVirtualPathTo("GVFlt_MoveFileTest\\PartialToOutside\\from\\lessInFrom.txt");
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            string fileOutsideRepoPath = Path.Combine(this.Enlistment.EnlistmentRoot, "FileRenamedOutOfRepoAddedToSparseCheckoutAndSkipWorktreeBitCleared.txt");
            this.fileSystem.MoveFile(fileToRenameVirtualPath, fileOutsideRepoPath);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToRenameSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToRenameSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
        }

        [TestCase, Order(9)]
        public void OverwrittenFileAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToOverwriteSparseCheckoutEntry = "/Test_EPF_WorkingDirectoryTests/1/2/3/4/ReadDeepProjectedFile.cpp";
            string fileToOverwriteVirtualPath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests\\1\\2\\3\\4\\ReadDeepProjectedFile.cpp");
            this.VerifyWorktreeBit(fileToOverwriteSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            string testContents = "Test contents for FileRenamedOutOfRepoWillBeAddedToSparseCheckoutAndHaveSkipWorktreeBitCleared";

            this.fileSystem.WriteAllText(fileToOverwriteVirtualPath, testContents);
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            fileToOverwriteVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(testContents);

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToOverwriteSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToOverwriteSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);
        }

        [TestCase, Order(10)]
        public void SupersededFileAddedToSparseCheckoutAndSkipWorktreeBitCleared()
        {
            string fileToSupersedeSparseCheckoutEntry = "/GVFlt_FileOperationTest/WriteAndVerify.txt";
            string fileToSupersedePath = this.Enlistment.GetVirtualPathTo("GVFlt_FileOperationTest\\WriteAndVerify.txt");
            this.VerifyWorktreeBit(fileToSupersedeSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.SkipWorktree);

            string newContent = "SupersededFileWillBeAddedToSparseCheckoutAndHaveSkipWorktreeBitCleared test new contents";

            SupersedeFile(fileToSupersedePath, newContent).ShouldEqual(true);
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            // Verify sparse-checkout contents
            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(fileToSupersedeSparseCheckoutEntry);

            // Verify skip-worktree cleared
            this.VerifyWorktreeBit(fileToSupersedeSparseCheckoutEntry.TrimStart(new char[] { '/' }), LsFilesStatus.Cached);

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

        private class GitFilesTestsRunners
        {
            public const string TestRunners = "Runners";

            public static object[] Runners
            {
                get
                {
                    // Don't use the BashRunner for GitFilesTests as the BashRunner always strips off the last trailing newline (\n)
                    // and we expect there to be a trailing new line
                    List<object[]> runners = new List<object[]>();
                    foreach (object[] runner in FileSystemRunner.Runners.ToList())
                    {
                        if (!(runner.ToList().First() is BashRunner))
                        {
                            runners.Add(new object[] { runner.ToList().First() });
                        }
                    }

                    return runners.ToArray();
                }
            }
        }
    }
}
