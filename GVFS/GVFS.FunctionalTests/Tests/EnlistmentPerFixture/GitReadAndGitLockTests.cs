using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class GitReadAndGitLockTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        public GitReadAndGitLockTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase, Order(1)]
        public void GitStatus()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "nothing to commit, working tree clean");
        }

        [TestCase, Order(2)]
        public void GitLog()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "log -n1", "commit", "Author:", "Date:");
        }

        [TestCase, Order(3)]
        public void GitBranch()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "branch -a",
                "* " + Properties.Settings.Default.Commitish,
                "remotes/origin/" + Properties.Settings.Default.Commitish);
        }

        [TestCase, Order(4)]
        public void GitCommandWaitsWhileAnotherIsRunning()
        {
            GitHelpers.AcquireGVFSLock(this.Enlistment, resetTimeout: 3000);

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }

        [TestCase, Order(5)]
        public void GitAliasNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasNamedAfterKnownCommandAcquiresLock);

            GitHelpers.AcquireGVFSLock(this.Enlistment, resetTimeout: 3000);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " status");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, alias, cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }

        [TestCase, Order(6)]
        public void GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock);

            GitHelpers.AcquireGVFSLock(this.Enlistment, resetTimeout: 3000);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " rebase");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(
                Path.Combine(this.Enlistment.RepoRoot, "GVFS"),
                alias + " origin/FunctionalTests/RebaseTestsSource_20170208",
                cleanErrors: false);
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "rebase --abort");
        }

        [TestCase, Order(7)]
        public void ExternalLockHolderReportedWhenBackgroundTasksArePending()
        {
            GitHelpers.AcquireGVFSLock(this.Enlistment, resetTimeout: 3000);

            // Creating a new file will queue a background task
            string newFilePath = this.Enlistment.GetVirtualPathTo("ExternalLockHolderReportedWhenBackgroundTasksArePending.txt");
            newFilePath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(newFilePath, "New file contents");

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", cleanErrors: false);

            // Validate that GVFS still reports that the git command is holding the lock
            statusWait.Errors.ShouldContain("Waiting for 'git hash-object --stdin");
        }
    }
}
