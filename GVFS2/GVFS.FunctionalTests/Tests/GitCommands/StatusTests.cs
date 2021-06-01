using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class StatusTests : GitRepoTests
    {
        public StatusTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            string srcPath = @"Readme.md";
            string dstPath = Path.Combine(".git", "destination.txt");

            this.MoveFile(srcPath, dstPath);
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void DeleteThenCreateThenDeleteFile()
        {
            string srcPath = @"Readme.md";

            this.DeleteFile(srcPath);
            this.ValidateGitCommand("status");
            this.CreateFile("Testing", srcPath);
            this.ValidateGitCommand("status");
            this.DeleteFile(srcPath);
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void CreateFileWithoutClose()
        {
            string srcPath = @"CreateFileWithoutClose.md";
            this.CreateFileWithoutClose(srcPath);
            this.ValidGitStatusWithRetry(srcPath);
        }

        [TestCase]
        public void WriteWithoutClose()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidGitStatusWithRetry(srcPath);
        }

         [TestCase]
         public void AppendFileUsingBash()
         {
            // Bash will perform the append using '>>' which will cause KAUTH_VNODE_APPEND_DATA to be sent without hydration
            // Other Runners may cause hydration before append
            BashRunner bash = new BashRunner();
            string filePath = Path.Combine("Test_EPF_UpdatePlaceholderTests", "LockToPreventUpdate", "test.txt");
            string content = "Apended Data";
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, filePath);
            string controlFile = Path.Combine(this.ControlGitRepo.RootPath, filePath);
            bash.AppendAllText(virtualFile, content);
            bash.AppendAllText(controlFile, content);

            this.ValidateGitCommand("status");

            // We check the contents after status, to ensure this check didn't cause the hydration
            string appendedContent = string.Concat("Commit2LockToPreventUpdate \r\n", content);
            virtualFile.ShouldBeAFile(this.FileSystem).WithContents(appendedContent);
            controlFile.ShouldBeAFile(this.FileSystem).WithContents(appendedContent);
        }

        [TestCase]
        [Category(Categories.MacTODO.NeedsStatusCache)]
        public void ModifyingAndDeletingRepositoryExcludeFileInvalidatesCache()
        {
            string repositoryExcludeFile = Path.Combine(".git", "info", "exclude");

            this.RepositoryIgnoreTestSetup();

            // Add ignore pattern to existing exclude file
            this.EditFile("*.ign", repositoryExcludeFile);

            // The exclude file has been modified, verify this status
            // excludes the "test.ign" file as expected.
            this.ValidateGitCommand("status");

            // Wait for status cache
            this.WaitForStatusCacheToBeGenerated();

            // Delete repository exclude file
            this.DeleteFile(repositoryExcludeFile);

            // The exclude file has been deleted, verify this status
            // includes the "test.ign" file as expected.
            this.ValidateGitCommand("status");
        }

        [TestCase]
        [Category(Categories.MacTODO.NeedsStatusCache)]
        public void NewRepositoryExcludeFileInvalidatesCache()
        {
            string repositoryExcludeFileRelativePath = Path.Combine(".git", "info", "exclude");
            string repositoryExcludeFilePath = Path.Combine(this.Enlistment.EnlistmentRoot, repositoryExcludeFileRelativePath);

            this.DeleteFile(repositoryExcludeFileRelativePath);

            this.RepositoryIgnoreTestSetup();

            File.Exists(repositoryExcludeFilePath).ShouldBeFalse("Repository exclude path should not exist");

            // Create new exclude file with ignore pattern
            this.CreateFile("*.ign", repositoryExcludeFileRelativePath);

            // The exclude file has been modified, verify this status
            // excludes the "test.ign" file as expected.
            this.ValidateGitCommand("status");
        }

        [TestCase]
        [Category(Categories.MacTODO.NeedsStatusCache)]
        public void ModifyingHeadSymbolicRefInvalidatesCache()
        {
            this.ValidateGitCommand("status");

            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);

            this.ValidateGitCommand("branch other_branch");

            this.WaitForStatusCacheToBeGenerated();
            this.ValidateGitCommand("status");

            this.ValidateGitCommand("symbolic-ref HEAD refs/heads/other_branch");
        }

        [TestCase]
        [Category(Categories.MacTODO.NeedsStatusCache)]
        public void ModifyingHeadRefInvalidatesCache()
        {
            this.ValidateGitCommand("status");

            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);

            this.ValidateGitCommand("update-ref HEAD HEAD~1");

            this.WaitForStatusCacheToBeGenerated();
            this.ValidateGitCommand("status");
        }

        private void RepositoryIgnoreTestSetup()
        {
            this.WaitForUpToDateStatusCache();

            string statusCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "GitStatusCache", "GitStatusCache.dat");
            File.Delete(statusCachePath);

            // Create a new file with an extension that will be ignored later in the test.
            this.CreateFile("file to be ignored", "test.ign");

            this.WaitForStatusCacheToBeGenerated();

            // Verify that status from the status cache includes the "test.ign" entry
            this.ValidateGitCommand("status");
        }

        /// <summary>
        /// Wait for an up-to-date status cache file to exist on disk.
        /// </summary>
        private void WaitForUpToDateStatusCache()
        {
            // Run "git status" for the side effect that it will delete any stale status cache file.
            this.ValidateGitCommand("status");

            // Wait for a new status cache to be generated.
            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);
        }

        private void WaitForStatusCacheToBeGenerated(bool waitForNewFile = true)
        {
            string statusCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "GitStatusCache", "GitStatusCache.dat");

            if (waitForNewFile)
            {
                File.Exists(statusCachePath).ShouldEqual(false, "Status cache file should not exist at this point - it should have been deleted by previous status command.");
            }

            // Wait for the status cache file to be regenerated
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(statusCachePath))
                {
                    break;
                }

                Thread.Sleep(1000);
            }

            // The cache file should exist by now. We want the next status to come from the
            // cache and include the "test.ign" entry.
            File.Exists(statusCachePath).ShouldEqual(true, "Status cache file should be regenerated by this point.");
        }

        private void ValidGitStatusWithRetry(string srcPath)
        {
            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.FileSystem, srcPath);
            try
            {
                this.ValidateGitCommand("status");
            }
            catch (Exception ex)
            {
                Thread.Sleep(1000);
                this.ValidateGitCommand("status");
                Assert.Fail("{0} was succesful on the second try, but failed on first: {1}", nameof(this.ValidateGitCommand), ex.Message);
            }
        }
    }
}
