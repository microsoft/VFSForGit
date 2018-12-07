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

        [TestCase, Order(1)]
        public void GitFsck()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "fsck");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }

        [TestCase, Order(2)]
        public void GitGc()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "gc");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
            result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "gc --auto");
            result.ExitCode.ShouldEqual(0, result.Errors);
        }

        [TestCase, Order(3)]
        public void GitPrune()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "prune");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }

        [TestCase, Order(4)]
        public void GitRepack()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "repack");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }

        [TestCase, Order(5)]
        public void GitSubmodule()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "submodule");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
            result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "submodule status");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }

        [TestCase, Order(6)]
        public void GitUpdateIndex()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "update-index --index-version 2");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
            result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "update-index --skip-worktree");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
            result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "update-index --no-skip-worktree");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
            result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "update-index --split-index");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }

        [TestCase, Order(7)]
        public void GitWorktree()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "worktree list");
            result.ExitCode.ShouldNotEqual(0, result.Errors);
        }
    }
}
