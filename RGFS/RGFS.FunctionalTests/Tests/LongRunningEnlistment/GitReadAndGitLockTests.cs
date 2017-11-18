using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class GitReadAndGitLockTests : TestsWithLongRunningEnlistment
    {
        private FileSystemRunner fileSystem;

        public GitReadAndGitLockTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase, Order(1)]
        public void GitStatus()
        {
            GitHelpers.CheckGitCommandAgainstRGFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "nothing to commit, working tree clean");
        }

        [TestCase, Order(2)]
        public void GitLog()
        {
            GitHelpers.CheckGitCommandAgainstRGFSRepo(this.Enlistment.RepoRoot, "log -n1", "commit", "Author:", "Date:");
        }

        [TestCase, Order(3)]
        public void GitBranch()
        {
            GitHelpers.CheckGitCommandAgainstRGFSRepo(
                this.Enlistment.RepoRoot,
                "branch -a",
                "* " + Properties.Settings.Default.Commitish,
                "remotes/origin/" + Properties.Settings.Default.Commitish);
        }

        [TestCase, Order(4)]
        public void GitCommandWaitsWhileAnotherIsRunning()
        {
            GitHelpers.AcquireRGFSLock(this.Enlistment, resetTimeout: 3000);

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status", cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }

        [TestCase, Order(5)]
        public void GitAliasNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasNamedAfterKnownCommandAcquiresLock);

            GitHelpers.AcquireRGFSLock(this.Enlistment, resetTimeout: 3000);
            GitHelpers.CheckGitCommandAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " status");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, alias, cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }

        [TestCase, Order(6)]
        public void GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock);

            GitHelpers.AcquireRGFSLock(this.Enlistment, resetTimeout: 3000);
            GitHelpers.CheckGitCommandAgainstRGFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " rebase");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstRGFSRepo(
                Path.Combine(this.Enlistment.RepoRoot, "RGFS"),
                alias + " origin/FunctionalTests/RebaseTestsSource_20170208",
                cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
            GitHelpers.CheckGitCommandAgainstRGFSRepo(this.Enlistment.RepoRoot, "rebase --abort");
        }

        [TestCase, Order(7)]
        public void ExternalLockHolderReportedWhenBackgroundTasksArePending()
        {
            GitHelpers.AcquireRGFSLock(this.Enlistment, resetTimeout: 3000);

            // Creating a new file will queue a background task
            string newFilePath = this.Enlistment.GetVirtualPathTo("ExternalLockHolderReportedWhenBackgroundTasksArePending.txt");
            newFilePath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(newFilePath, "New file contents");

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstRGFSRepo(this.Enlistment.RepoRoot, "status", cleanErrors: false);

            // Validate that RGFS still reports that the git command is holding the lock
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }
    }
}
