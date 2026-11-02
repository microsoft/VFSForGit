using GVFS.Tests.Should;
using NUnit.Framework;
using System.Linq;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class CommandLockingTests
    {
        [TestCase("annotate")]
        [TestCase("annotate --contents mock-file HEAD")]
        [TestCase("git-annotate")]
        [TestCase("blame")]
        [TestCase("branch")]
        [TestCase("cat-file")]
        [TestCase("check-attr")]
        [TestCase("check-ignore")]
        [TestCase("check-mailmap")]
        [TestCase("check-ref-format --branch topic")]
        [TestCase("check-ref-format --normalize refs/heads/topic")]
        [TestCase("check-ref-format --refspec-pattern refs/heads/*")]
        [TestCase("cherry -v HEAD~2 HEAD HEAD~1")]
        [TestCase("commit-graph")]
        [TestCase("config")]
        [TestCase("count-objects")]
        [TestCase("count-objects -vH")]
        [TestCase("credential")]
        [TestCase("diff")]
        [TestCase("diff-files")]
        [TestCase("diff-index")]
        [TestCase("diff-tree")]
        [TestCase("difftool")]
        [TestCase("fetch")]
        [TestCase("for-each-ref")]
        [TestCase("help")]
        [TestCase("hash-object")]
        [TestCase("index-pack")]
        [TestCase("log")]
        [TestCase("ls-files")]
        [TestCase("ls-remote --symref origin")]
        [TestCase("ls-remote --get-url origin")]
        [TestCase("ls-tree")]
        [TestCase("merge-base")]
        [TestCase("multi-pack-index")]
        [TestCase("name-rev")]
        [TestCase("pack-objects")]
        [TestCase("push")]
        [TestCase("range-diff HEAD~2..HEAD~1 HEAD~1..HEAD")]
        [TestCase("remote")]
        [TestCase("reset --soft")]
        [TestCase("rev-list")]
        [TestCase("rev-parse")]
        [TestCase("shortlog -sn HEAD")]
        [TestCase("show")]
        [TestCase("show-branch --list")]
        [TestCase("show-branch --merge-base")]
        [TestCase("show-branch --reflog")]
        [TestCase("show-ref")]
        [TestCase("submodule")]
        [TestCase("symbolic-ref")]
        [TestCase("tag")]
        [TestCase("unpack-objects")]
        [TestCase("update-ref")]
        [TestCase("verify-commit --raw -v HEAD")]
        [TestCase("verify-pack -v mock-pack.idx")]
        [TestCase("verify-pack -s mock-pack.idx")]
        [TestCase("verify-tag --raw -v mock-tag")]
        [TestCase("verify-tag --format=%(refname) mock-tag")]
        [TestCase("version")]
        [TestCase("web--browse")]
        [TestCase("worktree list")]
        [TestCase("worktree list --porcelain")]
        [TestCase("worktree list --porcelain -z")]
        [TestCase("worktree list --verbose --expire=now")]
        [TestCase("git-worktree list")]
        [TestCase("WORKTREE LIST")]
        [TestCase("worktree --git-pid=123 list")]
        public void CommandsWithoutLockPreserveTheirPolicy(string command)
        {
            AssertLockPolicy(command, shouldLock: false);
        }

        [TestCase("add")]
        [TestCase("apply")]
        [TestCase("checkout")]
        [TestCase("checkout-index")]
        [TestCase("cherry-pick")]
        [TestCase("clean")]
        [TestCase("commit")]
        [TestCase("describe")]
        [TestCase("describe --dirty")]
        [TestCase("describe --broken")]
        [TestCase("grep --open-files-in-pager=mock-pager pattern")]
        [TestCase("merge")]
        [TestCase("mv")]
        [TestCase("pull")]
        [TestCase("read-tree")]
        [TestCase("rebase")]
        [TestCase("reset")]
        [TestCase("reset --mixed")]
        [TestCase("reset --hard")]
        [TestCase("restore")]
        [TestCase("revert")]
        [TestCase("rm")]
        [TestCase("status")]
        [TestCase("status --no-lock-index")]
        [TestCase("update-index")]
        [TestCase("worktree")]
        [TestCase("worktree --git-pid=123")]
        [TestCase("worktree add -b branch list")]
        [TestCase("worktree remove list")]
        [TestCase("worktree move list new-path")]
        [TestCase("worktree lock list")]
        [TestCase("worktree unlock list")]
        [TestCase("worktree prune")]
        [TestCase("worktree repair")]
        [TestCase("worktree unknown list")]
        [TestCase("git-worktree add list")]
        [TestCase("write-tree")]
        public void CommandsWithLockPreserveTheirPolicy(string command)
        {
            AssertLockPolicy(command, shouldLock: true);
        }

        private static void AssertLockPolicy(string command, bool shouldLock)
        {
            foreach (string hook in new[] { "pre-command", "post-command" })
            {
                string[] args = new[] { hook }
                    .Concat(command.Split(' '))
                    .Concat(hook == "post-command"
                        ? new[] { "--git-pid=456", "--exit_code=0" }
                        : new[] { "--git-pid=456" })
                    .ToArray();

                GVFS.Hooks.Program.ShouldLock(args).ShouldEqual(shouldLock, string.Join(" ", args));
            }
        }
    }
}
