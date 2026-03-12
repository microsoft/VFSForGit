using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeCommandParserTests
    {
        [TestCase]
        public void GetSubcommandReturnsAdd()
        {
            string[] args = { "post-command", "worktree", "add", "-b", "branch", @"C:\wt" };
            WorktreeCommandParser.GetSubcommand(args).ShouldEqual("add");
        }

        [TestCase]
        public void GetSubcommandReturnsRemove()
        {
            string[] args = { "pre-command", "worktree", "remove", @"C:\wt" };
            WorktreeCommandParser.GetSubcommand(args).ShouldEqual("remove");
        }

        [TestCase]
        public void GetSubcommandSkipsLeadingDoubleHyphenArgs()
        {
            string[] args = { "post-command", "worktree", "--git-pid=1234", "add", @"C:\wt" };
            WorktreeCommandParser.GetSubcommand(args).ShouldEqual("add");
        }

        [TestCase]
        public void GetSubcommandReturnsNullWhenNoSubcommand()
        {
            string[] args = { "post-command", "worktree" };
            WorktreeCommandParser.GetSubcommand(args).ShouldBeNull();
        }

        [TestCase]
        public void GetSubcommandNormalizesToLowercase()
        {
            string[] args = { "post-command", "worktree", "Add" };
            WorktreeCommandParser.GetSubcommand(args).ShouldEqual("add");
        }

        [TestCase]
        public void GetPathArgExtractsPathFromAddWithBranch()
        {
            // git worktree add -b branch C:\worktree
            string[] args = { "post-command", "worktree", "add", "-b", "my-branch", @"C:\repos\wt", "--git-pid=123", "--exit_code=0" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgExtractsPathFromAddWithoutBranch()
        {
            // git worktree add C:\worktree
            string[] args = { "post-command", "worktree", "add", @"C:\repos\wt", "--git-pid=123", "--exit_code=0" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgExtractsPathFromRemove()
        {
            string[] args = { "pre-command", "worktree", "remove", @"C:\repos\wt", "--git-pid=456" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgExtractsPathFromRemoveWithForce()
        {
            string[] args = { "pre-command", "worktree", "remove", "--force", @"C:\repos\wt" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgSkipsBranchNameAfterDashB()
        {
            // -b takes a value — the path is the arg AFTER the branch name
            string[] args = { "post-command", "worktree", "add", "-b", "feature", @"C:\repos\feature" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\feature");
        }

        [TestCase]
        public void GetPathArgSkipsBranchNameAfterDashCapitalB()
        {
            string[] args = { "post-command", "worktree", "add", "-B", "feature", @"C:\repos\feature" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\feature");
        }

        [TestCase]
        public void GetPathArgSkipsAllOptionFlags()
        {
            // -f, -d, -q, --detach, --checkout, --lock, --no-checkout
            string[] args = { "post-command", "worktree", "add", "-f", "--no-checkout", "--lock", "--reason", "testing", @"C:\repos\wt" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgHandlesSeparator()
        {
            // After --, everything is positional
            string[] args = { "post-command", "worktree", "add", "--", @"C:\repos\wt" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }

        [TestCase]
        public void GetPathArgSkipsGitPidAndExitCode()
        {
            string[] args = { "post-command", "worktree", "add", @"C:\wt", "--git-pid=99", "--exit_code=0" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\wt");
        }

        [TestCase]
        public void GetPathArgReturnsNullWhenNoPath()
        {
            string[] args = { "post-command", "worktree", "list" };
            WorktreeCommandParser.GetPathArg(args).ShouldBeNull();
        }

        [TestCase]
        public void GetPositionalArgReturnsSecondPositional()
        {
            // git worktree move <worktree> <new-path>
            string[] args = { "post-command", "worktree", "move", @"C:\old", @"C:\new" };
            WorktreeCommandParser.GetPositionalArg(args, 0).ShouldEqual(@"C:\old");
            WorktreeCommandParser.GetPositionalArg(args, 1).ShouldEqual(@"C:\new");
        }

        [TestCase]
        public void GetPositionalArgReturnsNullForOutOfRangeIndex()
        {
            string[] args = { "post-command", "worktree", "remove", @"C:\wt" };
            WorktreeCommandParser.GetPositionalArg(args, 1).ShouldBeNull();
        }

        [TestCase]
        public void GetPathArgHandlesShortArgs()
        {
            // Ensure single-char flags without values are skipped
            string[] args = { "post-command", "worktree", "add", "-f", "-q", @"C:\repos\wt" };
            WorktreeCommandParser.GetPathArg(args).ShouldEqual(@"C:\repos\wt");
        }
    }
}
