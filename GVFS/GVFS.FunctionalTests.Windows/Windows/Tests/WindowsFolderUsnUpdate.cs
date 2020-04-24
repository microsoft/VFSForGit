using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tests.EnlistmentPerFixture;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Windows.Windows.Tests
{
    [TestFixture]
    [Category(Categories.WindowsOnly)]
    [Category(Categories.GitCommands)]
    [Ignore("fsutil requires WSL be enabled.  Need to find a way to enable for builds or a different way to get the USN for the folder.")]
    public class WindowsFolderUsnUpdate : TestsWithEnlistmentPerFixture
    {
        private const string StartingCommit = "ad87b3877c8fa6bebbe62330354f5c535875c4dd";
        private const string CommitWithChanges = "e6d047cf65f4a384568b7a451530e18410bc8a12";
        private FileSystemRunner fileSystem;

        public WindowsFolderUsnUpdate()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public void CheckoutUpdatesFolderUsnJournal()
        {
            this.GitCheckoutCommitId(StartingCommit);
            this.GitStatusShouldBeClean(StartingCommit);

            FolderPathUsn[] pathsToCheck = new FolderPathUsn[]
            {
                new FolderPathUsn(Path.Combine(this.Enlistment.RepoRoot, "Test_ConflictTests", "AddedFiles"), this.fileSystem),
                new FolderPathUsn(Path.Combine(this.Enlistment.RepoRoot, "Test_ConflictTests", "DeletedFiles"), this.fileSystem),
                new FolderPathUsn(Path.Combine(this.Enlistment.RepoRoot, "Test_ConflictTests", "ModifiedFiles"), this.fileSystem),
            };

            this.GitCheckoutCommitId(CommitWithChanges);
            this.GitStatusShouldBeClean(CommitWithChanges);

            foreach (FolderPathUsn folderPath in pathsToCheck)
            {
                folderPath.ValidateUsnChange();
            }
        }

        private static string UsnFolderId(string path)
        {
            ProcessResult result = ProcessHelper.Run("fsutil", $"usn readdata \"{path}\"");
            Match match = Regex.Match(result.Output, @"^Usn\s+:\s(\w+)", RegexOptions.Multiline);
            if (match.Success)
            {
                return match.Value;
            }

            return string.Empty;
        }

        private void GitStatusShouldBeClean(string commitId)
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "HEAD detached at " + commitId,
                "nothing to commit, working tree clean");
        }

        private void GitCheckoutCommitId(string commitId)
        {
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout " + commitId).Errors.ShouldContain("HEAD is now at " + commitId);
        }

        private class FolderPathUsn
        {
            private readonly string path;
            private readonly string originalUsn;

            public FolderPathUsn(string path, FileSystemRunner fileSystem)
            {
                this.path = path;
                fileSystem.EnumerateDirectory(path);
                this.originalUsn = UsnFolderId(path);
            }

            public void ValidateUsnChange()
            {
                string usnAfter = UsnFolderId(this.path);
                usnAfter.ShouldNotEqual(this.originalUsn);
            }
        }
    }
}
