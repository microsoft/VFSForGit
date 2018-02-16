using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(CategoryConstants.GitCommands)]
    public class DeleteEmptyFolderTests : GitRepoTests
    {
        public DeleteEmptyFolderTests() : base(enlistmentPerTest: true)
        {
        }

        [TestCase]
        [Ignore("Disabled until checkout cleans up now empty folders.")]
        public void VerifyResetHardDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("reset --hard HEAD");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, skipEmptyDirectories: false);
        }

        [TestCase]
        [Ignore("Disabled until checkout cleans up now empty folders.")]
        public void VerifyCleanDeletesEmptyFolders()
        {
            this.SetupFolderDeleteTest();

            this.RunGitCommand("clean -fd");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, skipEmptyDirectories: false);
        }

        private void SetupFolderDeleteTest()
        {
            ControlGitRepo.Fetch("FunctionalTests/20170202_RenameTestMergeTarget");
            this.ValidateGitCommand("checkout FunctionalTests/20170202_RenameTestMergeTarget");
            this.DeleteFile("Test_EPF_GitCommandsTestOnlyFileFolder\\file.txt");
            this.ValidateGitCommand("add .");
            this.RunGitCommand("commit -m\"Delete only file.\"");
        }
    }
}
