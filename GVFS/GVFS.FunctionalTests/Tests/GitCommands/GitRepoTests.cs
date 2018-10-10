using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    public abstract class GitRepoTests
    {
        protected const string ConflictSourceBranch = "FunctionalTests/20170206_Conflict_Source";
        protected const string ConflictTargetBranch = "FunctionalTests/20170206_Conflict_Target";
        protected const string NoConflictSourceBranch = "FunctionalTests/20170209_NoConflict_Source";
        protected const string DirectoryWithFileBeforeBranch = "FunctionalTests/20171025_DirectoryWithFileBefore";
        protected const string DirectoryWithFileAfterBranch = "FunctionalTests/20171025_DirectoryWithFileAfter";
        protected const string DirectoryWithDifferentFileAfterBranch = "FunctionalTests/20171025_DirectoryWithDifferentFile";
        protected const string DeepDirectoryWithOneFile = "FunctionalTests/20181010_DeepFolderOneFile";
        protected const string DeepDirectoryWithOneDifferentFile = "FunctionalTests/20181010_DeepFolderOneDifferentFile";

        private bool enlistmentPerTest;

        public GitRepoTests(bool enlistmentPerTest)
        {
            this.enlistmentPerTest = enlistmentPerTest;
            this.FileSystem = new SystemIORunner();
        }

        public ControlGitRepo ControlGitRepo
        {
            get; private set;
        }

        protected FileSystemRunner FileSystem
        {
            get; private set;
        }

        protected GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public virtual void SetupForFixture()
        {
            if (!this.enlistmentPerTest)
            {
                this.CreateEnlistment();
            }
        }

        [OneTimeTearDown]
        public virtual void TearDownForFixture()
        {
            if (!this.enlistmentPerTest)
            {
                this.DeleteEnlistment();
            }
        }

        [SetUp]
        public virtual void SetupForTest()
        {
            if (this.enlistmentPerTest)
            {
                this.CreateEnlistment();
            }

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);

            this.CheckHeadCommitTree();
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath);
            this.ValidateGitCommand("status");
        }

        [TearDown]
        public virtual void TearDownForTest()
        {
            this.TestValidationAndCleanup();
        }

        protected void TestValidationAndCleanup(bool ignoreCase = false)
        {
            try
            {
                this.CheckHeadCommitTree();
                this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                    .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, ignoreCase: ignoreCase);

                this.RunGitCommand("reset --hard -q HEAD");
                this.RunGitCommand("clean -d -f -x");
                this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);

                this.CheckHeadCommitTree();
                this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                    .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, ignoreCase: ignoreCase);
            }
            finally
            {
                if (this.enlistmentPerTest)
                {
                    this.DeleteEnlistment();
                }
            }
        }

        protected virtual void CreateEnlistment()
        {
            this.CreateEnlistment(null);
        }

        protected void CreateEnlistment(string commitish = null)
        {
            this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(GVFSTestConfig.PathToGVFS, commitish: commitish);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "config advice.statusUoption false");
            this.ControlGitRepo = ControlGitRepo.Create(commitish);
            this.ControlGitRepo.Initialize();
        }

        protected virtual void DeleteEnlistment()
        {
            if (this.Enlistment != null)
            {
                this.Enlistment.UnmountAndDeleteAll();
            }

            if (this.ControlGitRepo != null)
            {
                this.ControlGitRepo.Delete();
            }
        }

        protected void CheckHeadCommitTree()
        {
            this.ValidateGitCommand("ls-tree HEAD");
        }

        protected void RunGitCommand(string command, params object[] args)
        {
            this.RunGitCommand(string.Format(command, args));
        }

        /* We are using the following method for these scenarios
         * 1. Some commands compute a new commit sha, which is dependent on time and therefore
         *    won't match what is in the control repo.  For those commands, we just ensure that
         *    the errors match what we expect, but we skip comparing the output
         * 2. Using the sparse-checkout feature git will error out before checking the untracked files
         *    so the control repo will show the untracked files as being overwritten while the GVFS
         *    repo which is using the sparse-checkout will not.
         * 3. GVFS is returning not found for files that are outside the sparse-checkout and there
         *    are cases when git will delete these files during a merge outputting that it removed them
         *    which the GVFS repo did not have to remove so the message is missing that output.
         */
        protected void RunGitCommand(string command, bool ignoreErrors = false, bool checkStatus = true)
        {
            string controlRepoRoot = this.ControlGitRepo.RootPath;
            string gvfsRepoRoot = this.Enlistment.RepoRoot;

            ProcessResult expectedResult = GitProcess.InvokeProcess(controlRepoRoot, command);
            ProcessResult actualResult = GitHelpers.InvokeGitAgainstGVFSRepo(gvfsRepoRoot, command);
            if (!ignoreErrors)
            {
                GitHelpers.ErrorsShouldMatch(command, expectedResult, actualResult);
            }

            if (command != "status" && checkStatus)
            {
                this.ValidateGitCommand("status");
            }
        }

        protected void ValidateGitCommand(string command, params object[] args)
        {
            GitHelpers.ValidateGitCommand(
                this.Enlistment,
                this.ControlGitRepo,
                command,
                args);
        }

        protected void CreateEmptyFile()
        {
            string filePath = Path.GetRandomFileName() + "emptyFile.txt";
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.CreateEmptyFile(virtualFile);
            this.FileSystem.CreateEmptyFile(controlFile);
        }

        protected void CreateFile(string content, params string[] filePathPaths)
        {
            string filePath = Path.Combine(filePathPaths);
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.WriteAllText(virtualFile, content);
            this.FileSystem.WriteAllText(controlFile, content);
        }

        protected void CreateFolder(string folderPath)
        {
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            this.FileSystem.CreateDirectory(virtualFolder);
            this.FileSystem.CreateDirectory(controlFolder);
        }

        protected void EditFile(string content, params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.AppendAllText(virtualFile, content);
            this.FileSystem.AppendAllText(controlFile, content);
        }

        protected void CreateHardLink(string newLinkFileName, string existingFileName)
        {
            string virtualExistingFile = Path.Combine(this.Enlistment.RepoRoot, existingFileName);
            string controlExistingFile = Path.Combine(this.ControlGitRepo.RootPath, existingFileName);
            string virtualNewLinkFile = Path.Combine(this.Enlistment.RepoRoot, newLinkFileName);
            string controlNewLinkFile = Path.Combine(this.ControlGitRepo.RootPath, newLinkFileName);

            this.FileSystem.CreateHardLink(virtualNewLinkFile, virtualExistingFile);
            this.FileSystem.CreateHardLink(controlNewLinkFile, controlExistingFile);
        }

        protected void SetFileAsReadOnly(string filePath)
        {
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);

            File.SetAttributes(virtualFile, File.GetAttributes(virtualFile) | FileAttributes.ReadOnly);
            File.SetAttributes(virtualFile, File.GetAttributes(controlFile) | FileAttributes.ReadOnly);
        }

        protected void MoveFile(string pathFrom, string pathTo)
        {
            string virtualFileFrom = Path.Combine(this.Enlistment.RepoRoot, pathFrom);
            string virtualFileTo = Path.Combine(this.Enlistment.RepoRoot, pathTo);
            string controlFileFrom = Path.Combine(this.ControlGitRepo.RootPath, pathFrom);
            string controlFileTo = Path.Combine(this.ControlGitRepo.RootPath, pathTo);
            this.FileSystem.MoveFile(virtualFileFrom, virtualFileTo);
            this.FileSystem.MoveFile(controlFileFrom, controlFileTo);
            virtualFileFrom.ShouldNotExistOnDisk(this.FileSystem);
            controlFileFrom.ShouldNotExistOnDisk(this.FileSystem);
            virtualFileTo.ShouldBeAFile(this.FileSystem);
            controlFileTo.ShouldBeAFile(this.FileSystem);
        }

        protected void DeleteFile(params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.DeleteFile(virtualFile);
            this.FileSystem.DeleteFile(controlFile);
            virtualFile.ShouldNotExistOnDisk(this.FileSystem);
            controlFile.ShouldNotExistOnDisk(this.FileSystem);
        }

        protected void DeleteFolder(params string[] folderPathParts)
        {
            string folderPath = Path.Combine(folderPathParts);
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            this.FileSystem.DeleteDirectory(virtualFolder);
            this.FileSystem.DeleteDirectory(controlFolder);
            virtualFolder.ShouldNotExistOnDisk(this.FileSystem);
            controlFolder.ShouldNotExistOnDisk(this.FileSystem);
        }

        protected void MoveFolder(string pathFrom, string pathTo)
        {
            string virtualFileFrom = Path.Combine(this.Enlistment.RepoRoot, pathFrom);
            string virtualFileTo = Path.Combine(this.Enlistment.RepoRoot, pathTo);
            string controlFileFrom = Path.Combine(this.ControlGitRepo.RootPath, pathFrom);
            string controlFileTo = Path.Combine(this.ControlGitRepo.RootPath, pathTo);
            this.FileSystem.MoveDirectory(virtualFileFrom, virtualFileTo);
            this.FileSystem.MoveDirectory(controlFileFrom, controlFileTo);
            virtualFileFrom.ShouldNotExistOnDisk(this.FileSystem);
            controlFileFrom.ShouldNotExistOnDisk(this.FileSystem);
        }

        protected void FolderShouldExist(params string[] folderPathParts)
        {
            string folderPath = Path.Combine(folderPathParts);
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            virtualFolder.ShouldBeADirectory(this.FileSystem);
            controlFolder.ShouldBeADirectory(this.FileSystem);
        }

        protected void FolderShouldExistAndHaveFile(params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string folderPath = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileName(filePath);

            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            virtualFolder.ShouldBeADirectory(this.FileSystem).WithItems(fileName).Count().ShouldEqual(1);
            controlFolder.ShouldBeADirectory(this.FileSystem).WithItems(fileName).Count().ShouldEqual(1);
        }

        protected void FolderShouldExistAndBeEmpty(params string[] folderPathParts)
        {
            string folderPath = Path.Combine(folderPathParts);
            string virtualFolder = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolder = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            virtualFolder.ShouldBeADirectory(this.FileSystem).WithNoItems();
            controlFolder.ShouldBeADirectory(this.FileSystem).WithNoItems();
        }

        protected void ShouldNotExistOnDisk(params string[] pathParts)
        {
            string path = Path.Combine(pathParts);
            string virtualPath = Path.Combine(this.Enlistment.RepoRoot, path);
            string controlPath = Path.Combine(this.ControlGitRepo.RootPath, path);
            virtualPath.ShouldNotExistOnDisk(this.FileSystem);
            controlPath.ShouldNotExistOnDisk(this.FileSystem);
        }

        protected void FileShouldHaveContents(string contents, params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string virtualFilePath = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFilePath = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            virtualFilePath.ShouldBeAFile(this.FileSystem).WithContents(contents);
            controlFilePath.ShouldBeAFile(this.FileSystem).WithContents(contents);
        }

        protected void FileContentsShouldMatch(params string[] filePathPaths)
        {
            string filePath = Path.Combine(filePathPaths);
            string virtualFilePath = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFilePath = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            virtualFilePath.ShouldBeAFile(this.FileSystem).WithContents(controlFilePath.ShouldBeAFile(this.FileSystem).WithContents());
        }

        protected void FileShouldHaveCaseMatchingName(string filePath, string caseSensitiveName)
        {
            string virtualFilePath = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFilePath = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            virtualFilePath.ShouldBeAFile(this.FileSystem).WithCaseMatchingName(caseSensitiveName);
            controlFilePath.ShouldBeAFile(this.FileSystem).WithCaseMatchingName(caseSensitiveName);
        }

        protected void FolderShouldHaveCaseMatchingName(string folderPath, string caseSensitiveName)
        {
            string virtualFolderPath = Path.Combine(this.Enlistment.RepoRoot, folderPath);
            string controlFolderPath = Path.Combine(this.ControlGitRepo.RootPath, folderPath);
            virtualFolderPath.ShouldBeADirectory(this.FileSystem).WithCaseMatchingName(caseSensitiveName);
            controlFolderPath.ShouldBeADirectory(this.FileSystem).WithCaseMatchingName(caseSensitiveName);
        }

        protected void AppendAllText(string content, params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.AppendAllText(virtualFile, content);
            this.FileSystem.AppendAllText(controlFile, content);
        }

        protected void ReplaceText(string newContent, params string[] filePathParts)
        {
            string filePath = Path.Combine(filePathParts);
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            this.FileSystem.WriteAllText(virtualFile, newContent);
            this.FileSystem.WriteAllText(controlFile, newContent);
        }

        protected void SetupForFileDirectoryTest(string commandBranch = DirectoryWithFileAfterBranch)
        {
            this.ControlGitRepo.Fetch(DirectoryWithFileBeforeBranch);
            this.ControlGitRepo.Fetch(commandBranch);
            this.ValidateGitCommand($"checkout {DirectoryWithFileBeforeBranch}");
        }

        protected void ValidateFileDirectoryTest(string command, string commandBranch = DirectoryWithFileAfterBranch)
        {
            this.EditFile("Change file", "Readme.md");
            this.ValidateGitCommand("add --all");
            this.RunGitCommand("commit -m \"Some change\"");
            this.ValidateGitCommand($"{command} {commandBranch}");
        }

        protected void RunFileDirectoryEnumerateTest(string command, string commandBranch = DirectoryWithFileAfterBranch)
        {
            this.SetupForFileDirectoryTest(commandBranch);

            // file.txt is a folder with a file named file.txt to test checking out branches
            // that have folders with the same name as files
            this.FileSystem.EnumerateDirectory(this.Enlistment.GetVirtualPathTo("file.txt"));
            this.ValidateFileDirectoryTest(command, commandBranch);
        }

        protected void RunFileDirectoryReadTest(string command, string commandBranch = DirectoryWithFileAfterBranch)
        {
            this.SetupForFileDirectoryTest(commandBranch);
            this.FileContentsShouldMatch("file.txt", "file.txt");
            this.ValidateFileDirectoryTest(command, commandBranch);
        }

        protected void RunFileDirectoryWriteTest(string command, string commandBranch = DirectoryWithFileAfterBranch)
        {
            this.SetupForFileDirectoryTest(commandBranch);
            this.EditFile("Change file", "file.txt", "file.txt");
            this.ValidateFileDirectoryTest(command, commandBranch);
        }

        protected void ReadConflictTargetFiles()
        {
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothSameContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTargetDeleteInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ConflictingChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SameChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SuccessfulMerge.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "DeletedFiles", "DeleteInSource.txt");
        }

        protected void FilesShouldMatchCheckoutOfTargetBranch()
        {
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothSameContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "NoChange.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "DeletedFiles", "DeleteInSource.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTargetDeleteInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ConflictingChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SameChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SuccessfulMerge.txt");
        }

        protected void FilesShouldMatchCheckoutOfSourceBranch()
        {
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothSameContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedBySource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "NoChange.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "DeletedFiles", "DeleteInTarget.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSourceDeleteInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ConflictingChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SameChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SuccessfulMerge.txt");
        }

        protected void FilesShouldMatchAfterNoConflict()
        {
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothSameContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "NoChange.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTargetDeleteInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ConflictingChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SameChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SuccessfulMerge.txt");
        }

        protected void FilesShouldMatchAfterConflict()
        {
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothDifferentContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByBothSameContent.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedBySource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "AddedByTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "AddedFiles", "NoChange.txt");

            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInSourceDeleteInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ChangeInTargetDeleteInSource.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "ConflictingChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SameChange.txt");
            this.FileContentsShouldMatch("Test_ConflictTests", "ModifiedFiles", "SuccessfulMerge.txt");
        }
    }
}
