using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class StatusTests : GitRepoTests
    {
        public StatusTests(bool validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        [Category(Categories.MacTODO.FlakyTest)]
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
        [Category(Categories.MacTODO.M4)]
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
        [Category(Categories.MacTODO.M4)]
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
        [Category(Categories.MacTODO.M4)]
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
        [Category(Categories.MacTODO.M4)]
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
    }
}
