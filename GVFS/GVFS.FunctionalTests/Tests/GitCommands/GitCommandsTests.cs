using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class GitCommandsTests : GitRepoTests
    {
        public const string TopLevelFolderToCreate = "level1";
        private const string EncodingFileFolder = "FilenameEncoding";
        private const string EncodingFilename = "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt";
        private const string ContentWhenEditingFile = "// Adding a comment to the file";
        private const string UnknownTestName = "Unknown";
        private const string SubFolderToCreate = "level2";

        private static readonly string EditFilePath = Path.Combine("GVFS", "GVFS.Common", "GVFSContext.cs");
        private static readonly string DeleteFilePath = Path.Combine("GVFS", "GVFS", "Program.cs");
        private static readonly string RenameFilePathFrom = Path.Combine("GVFS", "GVFS.Common", "Physical", "FileSystem", "FileProperties.cs");
        private static readonly string RenameFilePathTo = Path.Combine("GVFS", "GVFS.Common", "Physical", "FileSystem", "FileProperties2.cs");
        private static readonly string RenameFolderPathFrom = Path.Combine("GVFS", "GVFS.Common", "PrefetchPacks");
        private static readonly string RenameFolderPathTo = Path.Combine("GVFS", "GVFS.Common", "PrefetchPacksRenamed");

        public GitCommandsTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: false, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void VerifyTestFilesExist()
        {
            // Sanity checks to ensure that the test files we expect to be in our test repo are present
            Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.EditFilePath).ShouldBeAFile(this.FileSystem);
            Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.EditFilePath).ShouldBeAFile(this.FileSystem);
            Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.DeleteFilePath).ShouldBeAFile(this.FileSystem);
            Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.RenameFilePathFrom).ShouldBeAFile(this.FileSystem);
            Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.RenameFolderPathFrom).ShouldBeADirectory(this.FileSystem);
        }

        [TestCase]
        public void StatusTest()
        {
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void StatusShortTest()
        {
            this.ValidateGitCommand("status -s");
        }

        [TestCase]
        public void BranchTest()
        {
            this.ValidateGitCommand("branch");
        }

        [TestCase]
        public void NewBranchTest()
        {
            this.ValidateGitCommand("branch tests/functional/NewBranchTest");
            this.ValidateGitCommand("branch");
        }

        [TestCase]
        public void DeleteBranchTest()
        {
            this.ValidateGitCommand("branch tests/functional/DeleteBranchTest");
            this.ValidateGitCommand("branch");
            this.ValidateGitCommand("branch -d tests/functional/DeleteBranchTest");
            this.ValidateGitCommand("branch");
        }

        [TestCase]
        public void RenameCurrentBranchTest()
        {
            this.ValidateGitCommand("checkout -b tests/functional/RenameBranchTest");
            this.ValidateGitCommand("branch -m tests/functional/RenameBranchTest2");
            this.ValidateGitCommand("branch");
        }

        [TestCase]
        public void UntrackedFileTest()
        {
            this.BasicCommit(this.CreateFile, addCommand: "add .");
        }

        [TestCase]
        public void UntrackedEmptyFileTest()
        {
            this.BasicCommit(this.CreateEmptyFile, addCommand: "add .");
        }

        [TestCase]
        public void UntrackedFileAddAllTest()
        {
            this.BasicCommit(this.CreateFile, addCommand: "add --all");
        }

        [TestCase]
        public void UntrackedEmptyFileAddAllTest()
        {
            this.BasicCommit(this.CreateEmptyFile, addCommand: "add --all");
        }

        [TestCase]
        public void StageUntrackedFileTest()
        {
            this.BasicCommit(this.CreateFile, addCommand: "stage .");
        }

        [TestCase]
        public void StageUntrackedEmptyFileTest()
        {
            this.BasicCommit(this.CreateEmptyFile, addCommand: "stage .");
        }

        [TestCase]
        public void StageUntrackedFileAddAllTest()
        {
            this.BasicCommit(this.CreateFile, addCommand: "stage --all");
        }

        [TestCase]
        public void StageUntrackedEmptyFileAddAllTest()
        {
            this.BasicCommit(this.CreateEmptyFile, addCommand: "stage --all");
        }

        [TestCase]
        public void CheckoutNewBranchTest()
        {
            this.ValidateGitCommand("checkout -b tests/functional/CheckoutNewBranchTest");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void CheckoutOrphanBranchTest()
        {
            this.ValidateGitCommand("checkout --orphan tests/functional/CheckoutOrphanBranchTest");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void CreateFileSwitchBranchTest()
        {
            this.SwitchBranch(fileSystemAction: this.CreateFile);
        }

        [TestCase]
        public void CreateFileStageChangesSwitchBranchTest()
        {
            this.StageChangesSwitchBranch(fileSystemAction: this.CreateFile);
        }

        [TestCase]
        public void CreateFileCommitChangesSwitchBranchTest()
        {
            this.CommitChangesSwitchBranch(fileSystemAction: this.CreateFile);
        }

        [TestCase]
        public void CreateFileCommitChangesSwitchBranchSwitchBranchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: this.CreateFile);
        }

        [TestCase]
        public void DeleteFileSwitchBranchTest()
        {
            this.SwitchBranch(fileSystemAction: this.DeleteFile);
        }

        [TestCase]
        public void DeleteFileStageChangesSwitchBranchTest()
        {
            this.StageChangesSwitchBranch(fileSystemAction: this.DeleteFile);
        }

        [TestCase]
        public void DeleteFileCommitChangesSwitchBranchTest()
        {
            this.CommitChangesSwitchBranch(fileSystemAction: this.DeleteFile);
        }

        [TestCase]
        public void DeleteFileCommitChangesSwitchBranchSwitchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: this.DeleteFile);
        }

        [TestCase]
        public void DeleteFileCommitChangesSwitchBranchSwitchBackDeleteFolderTest()
        {
            // 663045 - Confirm that folder can be deleted after deleting file then changing
            // branches
            string deleteFolderPath = Path.Combine("GVFlt_DeleteFolderTest", "GVFlt_DeletePlaceholderNonEmptyFolder_DeleteOnClose", "NonEmptyFolder");
            string deleteFilePath = Path.Combine(deleteFolderPath, "bar.txt");

            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: () => this.DeleteFile(deleteFilePath));
            this.DeleteFolder(deleteFolderPath);
        }

        [TestCase]
        public void DeleteFolderSwitchBranchTest()
        {
            this.SwitchBranch(fileSystemAction: () => this.DeleteFolder("GVFlt_DeleteFolderTest", "GVFlt_DeleteLocalEmptyFolder_DeleteOnClose"));
        }

        [TestCase]
        public void DeleteFolderStageChangesSwitchBranchTest()
        {
            this.StageChangesSwitchBranch(fileSystemAction: () => this.DeleteFolder("GVFlt_DeleteFolderTest", "GVFlt_DeleteLocalEmptyFolder_SetDisposition"));
        }

        [TestCase]
        public void DeleteFolderCommitChangesSwitchBranchTest()
        {
            this.CommitChangesSwitchBranch(fileSystemAction: () => this.DeleteFolder("GVFlt_DeleteFolderTest", "GVFlt_DeleteNonRootVirtualFolder_DeleteOnClose"));
        }

        [TestCase]
        public void DeleteFolderCommitChangesSwitchBranchSwitchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: () => this.DeleteFolder("GVFlt_DeleteFolderTest", "GVFlt_DeleteNonRootVirtualFolder_SetDisposition"));
        }

        [TestCase]
        public void DeleteFilesWithNameAheadOfDot()
        {
            string folder = Path.Combine("GitCommandsTests", "DeleteFileTests", "1");
            this.FolderShouldExistAndHaveFile(folder, "#test");
            this.DeleteFile(folder, "#test");
            this.FolderShouldExistAndBeEmpty(folder);

            folder = Path.Combine("GitCommandsTests", "DeleteFileTests", "2");
            this.FolderShouldExistAndHaveFile(folder, "$test");
            this.DeleteFile(folder, "$test");
            this.FolderShouldExistAndBeEmpty(folder);

            folder = Path.Combine("GitCommandsTests", "DeleteFileTests", "3");
            this.FolderShouldExistAndHaveFile(folder, ")");
            this.DeleteFile(folder, ")");
            this.FolderShouldExistAndBeEmpty(folder);

            folder = Path.Combine("GitCommandsTests", "DeleteFileTests", "4");
            this.FolderShouldExistAndHaveFile(folder, "+.test");
            this.DeleteFile(folder, "+.test");
            this.FolderShouldExistAndBeEmpty(folder);

            folder = Path.Combine("GitCommandsTests", "DeleteFileTests", "5");
            this.FolderShouldExistAndHaveFile(folder, "-.test");
            this.DeleteFile(folder, "-.test");
            this.FolderShouldExistAndBeEmpty(folder);

            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void RenameFilesWithNameAheadOfDot()
        {
            this.FolderShouldExistAndHaveFile("GitCommandsTests", "RenameFileTests", "1", "#test");
            this.MoveFile(
                Path.Combine("GitCommandsTests", "RenameFileTests", "1", "#test"),
                Path.Combine("GitCommandsTests", "RenameFileTests", "1", "#testRenamed"));

            this.FolderShouldExistAndHaveFile("GitCommandsTests", "RenameFileTests", "2", "$test");
            this.MoveFile(
                Path.Combine("GitCommandsTests", "RenameFileTests", "2", "$test"),
                Path.Combine("GitCommandsTests", "RenameFileTests", "2", "$testRenamed"));

            this.FolderShouldExistAndHaveFile("GitCommandsTests", "RenameFileTests", "3", ")");
            this.MoveFile(
                Path.Combine("GitCommandsTests", "RenameFileTests", "3", ")"),
                Path.Combine("GitCommandsTests", "RenameFileTests", "3", ")Renamed"));

            this.FolderShouldExistAndHaveFile("GitCommandsTests", "RenameFileTests", "4", "+.test");
            this.MoveFile(
                Path.Combine("GitCommandsTests", "RenameFileTests", "4", "+.test"),
                Path.Combine("GitCommandsTests", "RenameFileTests", "4", "+.testRenamed"));

            this.FolderShouldExistAndHaveFile("GitCommandsTests", "RenameFileTests", "5", "-.test");
            this.MoveFile(
                Path.Combine("GitCommandsTests", "RenameFileTests", "5", "-.test"),
                Path.Combine("GitCommandsTests", "RenameFileTests", "5", "-.testRenamed"));

            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void DeleteFileWithNameAheadOfDotAndSwitchCommits()
        {
            string fileRelativePath = Path.Combine("DeleteFileWithNameAheadOfDotAndSwitchCommits", "(1).txt");
            this.DeleteFile(fileRelativePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("checkout -- DeleteFileWithNameAheadOfDotAndSwitchCommits/(1).txt");
            this.DeleteFile(fileRelativePath);
            this.ValidateGitCommand("status");

            // 14cf226119766146b1fa5c5aa4cd0896d05f6b63 is the commit prior to creating (1).txt, it has two different files with
            // names that start with '(':
            // (a).txt
            // (z).txt
            this.ValidateGitCommand("checkout 14cf226119766146b1fa5c5aa4cd0896d05f6b63");
            this.DeleteFile("DeleteFileWithNameAheadOfDotAndSwitchCommits", "(a).txt");
            this.ValidateGitCommand("checkout -- DeleteFileWithNameAheadOfDotAndSwitchCommits/(a).txt");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void AddFileAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack()
        {
            // 663045 - Confirm that folder can be deleted after adding a file then changing branches
            string newFileParentFolderPath = Path.Combine("GVFS", "GVFS", "CommandLine");
            string newFilePath = Path.Combine(newFileParentFolderPath, "testfile.txt");
            string newFileContents = "test contents";

            this.CommitChangesSwitchBranch(
                fileSystemAction: () => this.CreateFile(newFileContents, newFilePath),
                test: "AddFileAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.DeleteFolder(newFileParentFolderPath);

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout tests/functional/AddFileAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            this.FolderShouldExist(newFileParentFolderPath);
            this.FileShouldHaveContents(newFileContents, newFilePath);
        }

        [TestCase]
        public void OverwriteFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack()
        {
            string overwrittenFileParentFolderPath = Path.Combine("GVFlt_DeleteFolderTest", "GVFlt_DeletePlaceholderNonEmptyFolder_SetDisposition");

            // GVFlt_DeleteFolderTest\GVFlt_DeletePlaceholderNonEmptyFolder_SetDispositiontestfile.txt already exists in the repo as TestFile.txt
            string fileToOverwritePath = Path.Combine(overwrittenFileParentFolderPath, "testfile.txt");
            string newFileContents = "test contents";

            this.CommitChangesSwitchBranch(
                fileSystemAction: () => this.CreateFile(newFileContents, fileToOverwritePath),
                test: "OverwriteFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.DeleteFolder(overwrittenFileParentFolderPath);

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout tests/functional/OverwriteFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            string subFolderPath = Path.Combine("GVFlt_DeleteFolderTest", "GVFlt_DeletePlaceholderNonEmptyFolder_SetDisposition", "NonEmptyFolder");
            this.ShouldNotExistOnDisk(subFolderPath);
            this.FolderShouldExist(overwrittenFileParentFolderPath);
            this.FileShouldHaveContents(newFileContents, fileToOverwritePath);
        }

        [TestCase]
        public void AddFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack()
        {
            // 663045 - Confirm that grandparent folder can be deleted after adding a (granchild) file
            // then changing branches
            string newFileParentFolderPath = Path.Combine("GVFlt_DeleteFolderTest", "GVFlt_DeleteVirtualNonEmptyFolder_DeleteOnClose", "NonEmptyFolder");
            string newFileGrandParentFolderPath = Path.Combine("GVFlt_DeleteFolderTest", "GVFlt_DeleteVirtualNonEmptyFolder_DeleteOnClose");
            string newFilePath = Path.Combine(newFileParentFolderPath, "testfile.txt");
            string newFileContents = "test contents";

            this.CommitChangesSwitchBranch(
                fileSystemAction: () => this.CreateFile(newFileContents, newFilePath),
                test: "AddFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.DeleteFolder(newFileGrandParentFolderPath);

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout tests/functional/AddFileInSubfolderAndCommitOnNewBranchSwitchDeleteFolderAndSwitchBack");

            this.FolderShouldExist(newFileParentFolderPath);
            this.FolderShouldExist(newFileGrandParentFolderPath);
            this.FileShouldHaveContents(newFileContents, newFilePath);
        }

        [TestCase]
        public void CommitWithNewlinesInMessage()
        {
            this.ValidateGitCommand("checkout -b tests/functional/commit_with_uncommon_arguments");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Message that contains \na\nnew\nline\"");
        }

        [TestCase]
        public void CaseOnlyRenameFileAndChangeBranches()
        {
            // 693190 - Confirm that file does not disappear after case-only rename and branch
            // changes
            string newBranchName = "tests/functional/CaseOnlyRenameFileAndChangeBranches";
            string oldFileName = "Readme.md";
            string newFileName = "README.md";

            this.ValidateGitCommand("checkout -b " + newBranchName);
            this.ValidateGitCommand("mv {0} {1}", oldFileName, newFileName);
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for CaseOnlyRenameFileAndChangeBranches\"");
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.FileShouldHaveCaseMatchingName(oldFileName);

            this.ValidateGitCommand("checkout " + newBranchName);
            this.FileShouldHaveCaseMatchingName(newFileName);
        }

        [TestCase]
        [Category(Categories.RepositoryMountsSameFileSystem)]
        public void MoveFileFromOutsideRepoToInsideRepoAndAdd()
        {
            string testFileContents = "0123456789";
            string filename = "MoveFileFromOutsideRepoToInsideRepo.cs";

            // Create the test files in this.Enlistment.EnlistmentRoot as it's outside of src and the control
            // repo and is cleaned up when the functional tests run
            string oldFilePath = Path.Combine(this.Enlistment.EnlistmentRoot, filename);
            string controlFilePath = Path.Combine(this.ControlGitRepo.RootPath, filename);
            string gvfsFilePath = Path.Combine(this.Enlistment.RepoRoot, filename);

            string newBranchName = "tests/functional/MoveFileFromOutsideRepoToInsideRepoAndAdd";
            this.ValidateGitCommand("checkout -b " + newBranchName);

            // Move file to control repo
            this.FileSystem.WriteAllText(oldFilePath, testFileContents);
            this.FileSystem.MoveFile(oldFilePath, controlFilePath);
            oldFilePath.ShouldNotExistOnDisk(this.FileSystem);
            controlFilePath.ShouldBeAFile(this.FileSystem).WithContents(testFileContents);

            // Move file to GVFS repo
            this.FileSystem.WriteAllText(oldFilePath, testFileContents);
            this.FileSystem.MoveFile(oldFilePath, gvfsFilePath);
            oldFilePath.ShouldNotExistOnDisk(this.FileSystem);
            gvfsFilePath.ShouldBeAFile(this.FileSystem).WithContents(testFileContents);

            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for MoveFileFromOutsideRepoToInsideRepoAndAdd\"");
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
        }

        [TestCase]
        [Category(Categories.RepositoryMountsSameFileSystem)]
        public void MoveFolderFromOutsideRepoToInsideRepoAndAdd()
        {
            string testFileContents = "0123456789";
            string filename = "MoveFolderFromOutsideRepoToInsideRepoAndAdd.cs";
            string folderName = "GitCommand_MoveFolderFromOutsideRepoToInsideRepoAndAdd";

            // Create the test folders in this.Enlistment.EnlistmentRoot as it's outside of src and the control
            // repo and is cleaned up when the functional tests run
            string oldFolderPath = Path.Combine(this.Enlistment.EnlistmentRoot, folderName);
            string oldFilePath = Path.Combine(this.Enlistment.EnlistmentRoot, folderName, filename);
            string controlFolderPath = Path.Combine(this.ControlGitRepo.RootPath, folderName);
            string gvfsFolderPath = Path.Combine(this.Enlistment.RepoRoot, folderName);

            string newBranchName = "tests/functional/MoveFolderFromOutsideRepoToInsideRepoAndAdd";
            this.ValidateGitCommand("checkout -b " + newBranchName);

            // Move folder to control repo
            this.FileSystem.CreateDirectory(oldFolderPath);
            this.FileSystem.WriteAllText(oldFilePath, testFileContents);
            this.FileSystem.MoveDirectory(oldFolderPath, controlFolderPath);
            oldFolderPath.ShouldNotExistOnDisk(this.FileSystem);
            Path.Combine(controlFolderPath, filename).ShouldBeAFile(this.FileSystem).WithContents(testFileContents);

            // Move folder to GVFS repo
            this.FileSystem.CreateDirectory(oldFolderPath);
            this.FileSystem.WriteAllText(oldFilePath, testFileContents);
            this.FileSystem.MoveDirectory(oldFolderPath, gvfsFolderPath);
            oldFolderPath.ShouldNotExistOnDisk(this.FileSystem);
            Path.Combine(gvfsFolderPath, filename).ShouldBeAFile(this.FileSystem).WithContents(testFileContents);

            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for MoveFolderFromOutsideRepoToInsideRepoAndAdd\"");
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
        }

        [TestCase]
        [Category(Categories.RepositoryMountsSameFileSystem)]
        public void MoveFileFromInsideRepoToOutsideRepoAndCommit()
        {
            string newBranchName = "tests/functional/MoveFileFromInsideRepoToOutsideRepoAndCommit";
            this.ValidateGitCommand("checkout -b " + newBranchName);

            // Confirm that no other test has caused "Protocol.md" to be added to the modified paths
            string fileName = "Protocol.md";
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.FileSystem, fileName);

            string controlTargetFolder = "MoveFileFromInsideRepoToOutsideRepoAndCommit_ControlTarget";
            string gvfsTargetFolder = "MoveFileFromInsideRepoToOutsideRepoAndCommit_GVFSTarget";

            // Create the target folders in this.Enlistment.EnlistmentRoot as it's outside of src and the control repo
            // and is cleaned up when the functional tests run
            string controlTargetFolderPath = Path.Combine(this.Enlistment.EnlistmentRoot, controlTargetFolder);
            string gvfsTargetFolderPath = Path.Combine(this.Enlistment.EnlistmentRoot, gvfsTargetFolder);
            string controlTargetFilePath = Path.Combine(controlTargetFolderPath, fileName);
            string gvfsTargetFilePath = Path.Combine(gvfsTargetFolderPath, fileName);

            // Move control repo file
            this.FileSystem.CreateDirectory(controlTargetFolderPath);
            this.FileSystem.MoveFile(Path.Combine(this.ControlGitRepo.RootPath, fileName), controlTargetFilePath);
            controlTargetFilePath.ShouldBeAFile(this.FileSystem);

            // Move GVFS repo file
            this.FileSystem.CreateDirectory(gvfsTargetFolderPath);
            this.FileSystem.MoveFile(Path.Combine(this.Enlistment.RepoRoot, fileName), gvfsTargetFilePath);
            gvfsTargetFilePath.ShouldBeAFile(this.FileSystem);

            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for MoveFileFromInsideRepoToOutsideRepoAndCommit\"");
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
        }

        [TestCase]
        public void EditFileSwitchBranchTest()
        {
            this.SwitchBranch(fileSystemAction: this.EditFile);
        }

        [TestCase]
        public void EditFileStageChangesSwitchBranchTest()
        {
            this.StageChangesSwitchBranch(fileSystemAction: this.EditFile);
        }

        [TestCase]
        public void EditFileCommitChangesSwitchBranchTest()
        {
            this.CommitChangesSwitchBranch(fileSystemAction: this.EditFile);
        }

        [TestCase]
        public void EditFileCommitChangesSwitchBranchSwitchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: this.EditFile);
        }

        [TestCase]
        public void RenameFileCommitChangesSwitchBranchSwitchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: this.RenameFile);
        }

        // Test applies only to platforms where renaming partial folders is allowed
        [TestCase]
        [Category(Categories.PartialFolderRenamesAllowed)]
        public void MoveFolderCommitChangesSwitchBranchSwitchBackTest()
        {
            this.CommitChangesSwitchBranchSwitchBack(fileSystemAction: this.MoveFolder);
        }

        // Mac and Linux only because Windows does not support file mode
        [TestCase]
        [Category(Categories.POSIXOnly)]
        public void UpdateFileModeOnly()
        {
            const string TestFileName = "test-file-mode";
            this.CreateFile("#!/bin/bash\n", TestFileName);
            this.ChangeMode(TestFileName, Convert.ToUInt16("755", 8));
            this.ValidateGitCommand($"add {TestFileName}");
            this.ValidateGitCommand($"ls-files --stage {TestFileName}");
        }

        [TestCase]
        public void AddFileCommitThenDeleteAndCommit()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndCommit_before");
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndCommit_after");
            string filePath = Path.Combine("GVFS", "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.DeleteFile(filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Delete file for AddFileCommitThenDeleteAndCommit\"");
            this.ValidateGitCommand("checkout tests/functional/AddFileCommitThenDeleteAndCommit_before");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
               .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);
            this.ValidateGitCommand("checkout tests/functional/AddFileCommitThenDeleteAndCommit_after");
        }

        [TestCase]
        public void AddFileCommitThenDeleteAndResetSoft()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string filePath = Path.Combine("GVFS", "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.DeleteFile(filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --soft HEAD~1");
        }

        [TestCase]
        public void AddFileCommitThenDeleteAndResetMixed()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string filePath = Path.Combine("GVFS", "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.DeleteFile(filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --soft HEAD~1");
        }

        [TestCase]
        public void AddFolderAndFileCommitThenDeleteAndResetSoft()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string folderPath = "test_folder";
            this.CreateFolder(folderPath);
            string filePath = Path.Combine(folderPath, "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.DeleteFile(filePath);
            this.DeleteFolder(folderPath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --soft HEAD~1");
        }

        [TestCase]
        public void AddFolderAndFileCommitThenDeleteAndResetMixed()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string folderPath = "test_folder";
            this.CreateFolder(folderPath);
            string filePath = Path.Combine(folderPath, "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.DeleteFile(filePath);
            this.DeleteFolder(folderPath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --mixed HEAD~1");
        }

        [TestCase]
        public void AddFolderAndFileCommitThenResetSoftAndResetHard()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string folderPath = "test_folder";
            this.CreateFolder(folderPath);
            string filePath = Path.Combine(folderPath, "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --soft HEAD~1");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void AddFolderAndFileCommitThenResetSoftAndResetMixed()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileCommitThenDeleteAndResetSoft");
            string folderPath = "test_folder";
            this.CreateFolder(folderPath);
            string filePath = Path.Combine(folderPath, "testfile.txt");
            this.CreateFile("Some new content for the file", filePath);
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for AddFileCommitThenDeleteAndCommit\"");
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --soft HEAD~1");
            this.ValidateGitCommand("reset --mixed HEAD");
        }

        [TestCase]
        public void AddFoldersAndFilesAndRenameFolder()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFoldersAndFilesAndRenameFolder");

            string topMostNewFolder = "AddFoldersAndFilesAndRenameFolder_Test";
            this.CreateFolder(topMostNewFolder);
            this.CreateFile("test contents", topMostNewFolder, "top_level_test_file.txt");

            string testFolderLevel1 = Path.Combine(topMostNewFolder, "TestFolderLevel1");
            this.CreateFolder(testFolderLevel1);
            this.CreateFile("test contents", testFolderLevel1, "level_1_test_file.txt");

            string testFolderLevel2 = Path.Combine(testFolderLevel1, "TestFolderLevel2");
            this.CreateFolder(testFolderLevel2);
            this.CreateFile("test contents", testFolderLevel2, "level_2_test_file.txt");

            string testFolderLevel3 = Path.Combine(testFolderLevel2, "TestFolderLevel3");
            this.CreateFolder(testFolderLevel3);
            this.CreateFile("test contents", testFolderLevel3, "level_3_test_file.txt");
            this.ValidateGitCommand("status");

            this.MoveFolder(testFolderLevel3, Path.Combine(testFolderLevel2, "TestFolderLevel3Renamed"));
            this.ValidateGitCommand("status");

            this.MoveFolder(testFolderLevel2, Path.Combine(testFolderLevel1, "TestFolderLevel2Renamed"));
            this.ValidateGitCommand("status");

            this.MoveFolder(testFolderLevel1, Path.Combine(topMostNewFolder, "TestFolderLevel1Renamed"));
            this.ValidateGitCommand("status");

            this.MoveFolder(topMostNewFolder, "AddFoldersAndFilesAndRenameFolder_TestRenamed");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void AddFileAfterFolderRename()
        {
            this.ValidateGitCommand("checkout -b tests/functional/AddFileAfterFolderRename");

            string folder = "AddFileAfterFolderRename_Test";
            string renamedFolder = "AddFileAfterFolderRename_TestRenamed";
            this.CreateFolder(folder);
            this.MoveFolder(folder, renamedFolder);
            this.CreateFile("test contents", renamedFolder, "test_file.txt");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void ResetSoft()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetSoft");
            this.ValidateGitCommand("reset --soft HEAD~1");
        }

        [TestCase]
        public void ResetMixed()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetMixed");
            this.ValidateGitCommand("reset --mixed HEAD~1");
        }

        [TestCase]
        public void ResetMixed2()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetMixed2");
            this.ValidateGitCommand("reset HEAD~1");
        }

        [TestCase]
        public void ManuallyModifyHead()
        {
            this.ValidateGitCommand("status");
            this.ReplaceText("f1bce402a7a980a8320f3f235cf8c8fdade4b17a", TestConstants.DotGit.Head);
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void ResetSoftTwice()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetSoftTwice");

            // A folder rename occured between 99fc72275f950b0052c8548bbcf83a851f2b4467 and
            // the subsequent commit 60d19c87328120d11618ad563c396044a50985b2
            this.ValidateGitCommand("reset --soft 60d19c87328120d11618ad563c396044a50985b2");
            this.ValidateGitCommand("reset --soft 99fc72275f950b0052c8548bbcf83a851f2b4467");
        }

        [TestCase]
        public void ResetMixedTwice()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetMixedTwice");

            // A folder rename occured between 99fc72275f950b0052c8548bbcf83a851f2b4467 and
            // the subsequent commit 60d19c87328120d11618ad563c396044a50985b2
            this.ValidateGitCommand("reset --mixed 60d19c87328120d11618ad563c396044a50985b2");
            this.ValidateGitCommand("reset --mixed 99fc72275f950b0052c8548bbcf83a851f2b4467");
        }

        [TestCase]
        public void ResetMixed2Twice()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetMixed2Twice");

            // A folder rename occured between 99fc72275f950b0052c8548bbcf83a851f2b4467 and
            // the subsequent commit 60d19c87328120d11618ad563c396044a50985b2
            this.ValidateGitCommand("reset 60d19c87328120d11618ad563c396044a50985b2");
            this.ValidateGitCommand("reset 99fc72275f950b0052c8548bbcf83a851f2b4467");
        }

        [TestCase]
        public void ResetHardAfterCreate()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterCreate");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ResetHardAfterEdit()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterEdit");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ResetHardAfterDelete()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterDelete");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ResetHardAfterCreateAndAdd()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterCreateAndAdd");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ResetHardAfterEditAndAdd()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterEditAndAdd");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ResetHardAfterDeleteAndAdd()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ResetHardAfterDeleteAndAdd");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.ValidateGitCommand("reset --hard HEAD");
        }

        [TestCase]
        public void ChangeTwoBranchesAndMerge()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ChangeTwoBranchesAndMerge_1");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for ChangeTwoBranchesAndMerge first branch\"");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout -b tests/functional/ChangeTwoBranchesAndMerge_2");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for ChangeTwoBranchesAndMerge second branch\"");
            this.ValidateGitCommand("merge tests/functional/ChangeTwoBranchesAndMerge_1");
        }

        [TestCase]
        public void ChangeBranchAndCherryPickIntoAnotherBranch()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchesAndCherryPickIntoAnotherBranch_1");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Create for ChangeBranchesAndCherryPickIntoAnotherBranch first branch\"");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Delete for ChangeBranchesAndCherryPickIntoAnotherBranch first branch\"");
            this.ValidateGitCommand("tag DeleteForCherryPick");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Edit for ChangeBranchesAndCherryPickIntoAnotherBranch first branch\"");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchesAndCherryPickIntoAnotherBranch_2");
            this.RunGitCommand("cherry-pick -q DeleteForCherryPick");
        }

        [TestCase]
        public void ChangeBranchAndMergeRebaseOnAnotherBranch()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchAndMergeRebaseOnAnotherBranch_1");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Create for ChangeBranchAndMergeRebaseOnAnotherBranch first branch\"");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Delete for ChangeBranchAndMergeRebaseOnAnotherBranch first branch\"");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchAndMergeRebaseOnAnotherBranch_2");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Edit for ChangeBranchAndMergeRebaseOnAnotherBranch first branch\"");

            this.RunGitCommand("rebase --merge tests/functional/ChangeBranchAndMergeRebaseOnAnotherBranch_1", ignoreErrors: true);
            this.ValidateGitCommand("rev-parse HEAD^{{tree}}");
        }

        [TestCase]
        public void ChangeBranchAndRebaseOnAnotherBranch()
        {
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchAndRebaseOnAnotherBranch_1");
            this.CreateFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Create for ChangeBranchAndRebaseOnAnotherBranch first branch\"");
            this.DeleteFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Delete for ChangeBranchAndRebaseOnAnotherBranch first branch\"");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout -b tests/functional/ChangeBranchAndRebaseOnAnotherBranch_2");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Edit for ChangeBranchAndRebaseOnAnotherBranch first branch\"");

            this.RunGitCommand("rebase tests/functional/ChangeBranchAndRebaseOnAnotherBranch_1", ignoreErrors: true);
            this.ValidateGitCommand("rev-parse HEAD^{{tree}}");
        }

        [TestCase]
        public void StashChanges()
        {
            this.ValidateGitCommand("checkout -b tests/functional/StashChanges");
            this.EditFile();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.ValidateGitCommand("stash");

            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.ValidateGitCommand("checkout -b tests/functional/StashChanges_2");
            this.RunGitCommand("stash pop");
        }

        [TestCase]
        public void OpenFileThenCheckout()
        {
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.EditFilePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, GitCommandsTests.EditFilePath);

            // Open files with ReadWrite sharing because depending on the state of the index (and the mtimes), git might need to read the file
            // as part of status (while we have the handle open).
            using (FileStream virtualFS = File.Open(virtualFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter virtualWriter = new StreamWriter(virtualFS))
            using (FileStream controlFS = File.Open(controlFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter controlWriter = new StreamWriter(controlFS))
            {
                this.ValidateGitCommand("checkout -b tests/functional/OpenFileThenCheckout");
                virtualWriter.WriteLine("// Adding a line for testing purposes");
                controlWriter.WriteLine("// Adding a line for testing purposes");
                this.ValidateGitCommand("status");
            }

            // NOTE: Due to optimizations in checkout -b, the modified files will not be included as part of the
            // success message.  Validate that the succcess messages match, and the call to validate "status" below
            // will ensure that GVFS is still reporting the edited file as modified.

            string controlRepoRoot = this.ControlGitRepo.RootPath;
            string gvfsRepoRoot = this.Enlistment.RepoRoot;
            string command = "checkout -b tests/functional/OpenFileThenCheckout_2";
            ProcessResult expectedResult = GitProcess.InvokeProcess(controlRepoRoot, command);
            ProcessResult actualResult = GitHelpers.InvokeGitAgainstGVFSRepo(gvfsRepoRoot, command);
            GitHelpers.ErrorsShouldMatch(command, expectedResult, actualResult);
            actualResult.Errors.ShouldContain("Switched to a new branch");

            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void EditFileNeedingUtf8Encoding()
        {
            this.ValidateGitCommand("checkout -b tests/functional/EditFileNeedingUtf8Encoding");
            this.ValidateGitCommand("status");

            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, EncodingFileFolder, EncodingFilename);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, EncodingFileFolder, EncodingFilename);
            string relativeGitPath = EncodingFileFolder + "/" + EncodingFilename;

            string contents = virtualFile.ShouldBeAFile(this.FileSystem).WithContents();
            string expectedContents = controlFile.ShouldBeAFile(this.FileSystem).WithContents();
            contents.ShouldEqual(expectedContents);

            // Confirm that the entry is not in the the modified paths database
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.FileSystem, relativeGitPath);
            this.ValidateGitCommand("status");

            this.AppendAllText(ContentWhenEditingFile, virtualFile);
            this.AppendAllText(ContentWhenEditingFile, controlFile);

            this.ValidateGitCommand("status");

            // Confirm that the entry was added to the modified paths database
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.FileSystem, relativeGitPath);
        }

        [TestCase]
        public void ChangeTimestampAndDiff()
        {
            // User scenario -
            // 1. Enlistment's "diff.autoRefreshIndex" config is set to false
            // 2. A checked out file got into a state where it differs from the git copy
            // only in its LastWriteTime metadata (no change in file contents.)
            // Repro steps - This happens when user edits a file, saves it and later decides
            // to undo the edit and save the file again.
            // Once in this state, the unchanged file (only its timestamp has changed) shows
            // up in `git difftool` creating noise. It also shows up in `git diff --raw` command,
            // (but not in `git status` or `git diff`.)

            // Change the timestamp - The lastwrite time can be close to the time this test method gets
            // run. Changing (Subtracting) it to the past so there will always be a difference.
            this.AdjustLastWriteTime(GitCommandsTests.EditFilePath, TimeSpan.FromDays(-10));
            this.ValidateGitCommand("diff --raw");
            this.ValidateGitCommand($"checkout {GitCommandsTests.EditFilePath}");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void UseAlias()
        {
            this.ValidateGitCommand("config --local alias.potato status");
            this.ValidateGitCommand("potato");
        }

        [TestCase]
        public void RenameOnlyFileInFolder()
        {
            this.ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeTarget");
            this.ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeSource");

            this.ValidateGitCommand("checkout FunctionalTests/20170202_RenameTestMergeTarget");
            this.FileSystem.ReadAllText(this.Enlistment.GetVirtualPathTo("Test_EPF_GitCommandsTestOnlyFileFolder", "file.txt"));
            this.ValidateGitCommand("merge origin/FunctionalTests/20170202_RenameTestMergeSource");
        }

        [TestCase]
        public void BlameTest()
        {
            this.ValidateGitCommand("blame Readme.md");
        }

        private void BasicCommit(Action fileSystemAction, string addCommand, [CallerMemberName]string test = GitCommandsTests.UnknownTestName)
        {
            this.ValidateGitCommand($"checkout -b tests/functional/{test}");
            fileSystemAction();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand(addCommand);
            this.RunGitCommand($"commit -m \"BasicCommit for {test}\"");
        }

        private void SwitchBranch(Action fileSystemAction, [CallerMemberName]string test = GitCommandsTests.UnknownTestName)
        {
            this.ValidateGitCommand("checkout -b tests/functional/{0}", test);
            fileSystemAction();
            this.ValidateGitCommand("status");
        }

        private void StageChangesSwitchBranch(Action fileSystemAction, [CallerMemberName]string test = GitCommandsTests.UnknownTestName)
        {
            this.ValidateGitCommand("checkout -b tests/functional/{0}", test);
            fileSystemAction();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
        }

        private void CommitChangesSwitchBranch(Action fileSystemAction, [CallerMemberName]string test = GitCommandsTests.UnknownTestName)
        {
            this.ValidateGitCommand("checkout -b tests/functional/{0}", test);
            fileSystemAction();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for {0}\"", test);
        }

        private void CommitChangesSwitchBranchSwitchBack(Action fileSystemAction, [CallerMemberName]string test = GitCommandsTests.UnknownTestName)
        {
            string branch = string.Format("tests/functional/{0}", test);
            this.ValidateGitCommand("checkout -b {0}", branch);
            fileSystemAction();
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m \"Change for {0}\"", branch);
            this.ValidateGitCommand("checkout " + this.ControlGitRepo.Commitish);
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);

            this.ValidateGitCommand("checkout {0}", branch);
        }

        private void CreateFile()
        {
            this.CreateFile("Some content here", Path.GetRandomFileName() + "tempFile.txt");
            this.CreateFolder(TopLevelFolderToCreate);
            this.CreateFolder(Path.Combine(TopLevelFolderToCreate, SubFolderToCreate));
            this.CreateFile("File in new folder", Path.Combine(TopLevelFolderToCreate, SubFolderToCreate, Path.GetRandomFileName() + "folderFile.txt"));
        }

        private void EditFile()
        {
            this.AppendAllText(ContentWhenEditingFile, GitCommandsTests.EditFilePath);
        }

        private void DeleteFile()
        {
            this.DeleteFile(GitCommandsTests.DeleteFilePath);
        }

        private void RenameFile()
        {
            string virtualFileFrom = Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.RenameFilePathFrom);
            string virtualFileTo = Path.Combine(this.Enlistment.RepoRoot, GitCommandsTests.RenameFilePathTo);
            string controlFileFrom = Path.Combine(this.ControlGitRepo.RootPath, GitCommandsTests.RenameFilePathFrom);
            string controlFileTo = Path.Combine(this.ControlGitRepo.RootPath, GitCommandsTests.RenameFilePathTo);
            this.FileSystem.MoveFile(virtualFileFrom, virtualFileTo);
            this.FileSystem.MoveFile(controlFileFrom, controlFileTo);
            virtualFileFrom.ShouldNotExistOnDisk(this.FileSystem);
            controlFileFrom.ShouldNotExistOnDisk(this.FileSystem);
        }

        private void MoveFolder()
        {
            this.MoveFolder(GitCommandsTests.RenameFolderPathFrom, GitCommandsTests.RenameFolderPathTo);
        }
    }
}
