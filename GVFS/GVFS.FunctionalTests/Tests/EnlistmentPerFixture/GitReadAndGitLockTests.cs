using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class GitReadAndGitLockTests : TestsWithEnlistmentPerFixture
    {
        private const string ExpectedStatusWaitingText = @"Waiting for 'GVFS.FunctionalTests.LockHolder'";
        private const int AcquireGVFSLockTimeout = 10 * 1000;
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
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 3000);

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(5)]
        public void GitAliasNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasNamedAfterKnownCommandAcquiresLock);

            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: AcquireGVFSLockTimeout);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " status");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, alias, removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(6)]
        public void GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock);

            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: AcquireGVFSLockTimeout);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " rebase");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(
                Path.Combine(this.Enlistment.RepoRoot, "GVFS"),
                alias + " origin/FunctionalTests/RebaseTestsSource_20170208",
                removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "rebase --abort");
        }

        [TestCase, Order(7)]
        public void ExternalLockHolderReportedWhenBackgroundTasksArePending()
        {
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 3000);

            // Creating a new file will queue a background task
            string newFilePath = this.Enlistment.GetVirtualPathTo("ExternalLockHolderReportedWhenBackgroundTasksArePending.txt");
            newFilePath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(newFilePath, "New file contents");

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);

            // Validate that GVFS still reports that the git command is holding the lock
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(8)]
        public void OrphanedGVFSLockIsCleanedUp()
        {
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 1000, skipReleaseLock: true);

            while (true)
            {
                try
                {
                    using (Process.GetProcessById(pid))
                    {
                    }

                    Thread.Sleep(1000);
                }
                catch (ArgumentException)
                {
                    break;
                }
            }

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);

            // There should not be any errors - in particular, there should not be
            // an error about "Waiting for GVFS.FunctionalTests.LockHolder"
            statusWait.Errors.ShouldEqual(string.Empty);
        }
    }
}
