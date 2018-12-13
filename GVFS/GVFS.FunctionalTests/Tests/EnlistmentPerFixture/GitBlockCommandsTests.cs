using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
    [Category(Categories.GitCommands)]
    public class GitBlockCommandsTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;
        public GitBlockCommandsTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase]
        public void GitBlockCommands()
        {
            this.CommandBlocked("fsck");
            this.CommandBlocked("gc");
            this.CommandNotBlocked("gc --auto");
            this.CommandBlocked("prune");
            this.CommandBlocked("prune");
            this.CommandBlocked("repack");
            this.CommandBlocked("submodule");
            this.CommandBlocked("submodule status");
            this.CommandBlocked("update-index --index-version 2");
            this.CommandBlocked("update-index --skip-worktree");
            this.CommandBlocked("update-index --no-skip-worktree");
            this.CommandBlocked("update-index --split-index");
            this.CommandBlocked("worktree list");
        }

        private void CommandBlocked(string command)
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                command);
            result.ExitCode.ShouldNotEqual(0, $"Command {command} not blocked when it should be.  Errors: {result.Errors}");
        }

        private void CommandNotBlocked(string command)
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                command);
            result.ExitCode.ShouldEqual(0, $"Command {command}  blocked when it should not be.  Errors: {result.Errors}");
        }
    }
}
