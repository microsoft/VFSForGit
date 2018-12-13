using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class GitBlockCommandsTests : TestsWithEnlistmentPerFixture
    {
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
