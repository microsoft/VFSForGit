using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class CommandLockingTests
    {
        [TestCase("annotate")]
        [TestCase("annotate --contents mock-file HEAD")]
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
        [TestCase("git-submodule")]
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
        [TestCase("git-web--browse")]
        [TestCase("worktree list")]
        [TestCase("worktree list --porcelain")]
        [TestCase("worktree list --porcelain -z")]
        [TestCase("worktree list --verbose --expire=now")]
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
        [TestCase("write-tree")]
        public void CommandsWithLockPreserveTheirPolicy(string command)
        {
            AssertLockPolicy(command, shouldLock: true);
        }

        [TestCase("git-annotate")]
        [TestCase("git-DIFF")]
        [TestCase("git-LOG")]
        [TestCase("git-worktree list")]
        [TestCase("git-worktree add list")]
        [TestCase("git-reset --soft")]
        public void ExternalsNamedAfterBuiltInsAcquireLock(string command)
        {
            string hookCommand = command.Split(' ')[0];
            string[] args = CreatePreCommandArgs(command);
            List<string> candidates = new List<string>();

            GVFS.Hooks.Program.TryShouldLock(
                args,
                () => throw new AssertionException("A program must not query aliases."),
                @"mock:\tools",
                path =>
                {
                    candidates.Add(path);
                    return 0x80;
                },
                out bool shouldLock,
                out string error).ShouldBeTrue();

            shouldLock.ShouldBeTrue(string.Join(" ", args));
            error.ShouldBeNull();
            candidates.ShouldMatchInOrder(@"mock:\tools\" + hookCommand + ".exe");
        }

        [TestCase("git-LOG")]
        [TestCase("git-co")]
        public void AliasesWithoutAProgramSkipTheOuterLock(string hookCommand)
        {
            int queries = 0;
            GVFS.Hooks.Program.TryShouldLock(
                CreatePreCommandArgs(hookCommand),
                () =>
                {
                    ++queries;
                    return new ProcessResult("alias." + hookCommand.Substring("git-".Length).ToLowerInvariant() + "\n", string.Empty, 0);
                },
                @"mock:\tools",
                path => GVFS.Hooks.GitCommandResolver.InvalidFileAttributes,
                out bool shouldLock,
                out string error).ShouldBeTrue();

            queries.ShouldEqual(1);
            shouldLock.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase]
        public void AliasQueryFailuresAreReturnedToTheHook()
        {
            GVFS.Hooks.Program.TryShouldLock(
                CreatePreCommandArgs("git-MiXeD"),
                () => new ProcessResult(string.Empty, "fatal: bad config", 128),
                @"mock:\tools",
                path => GVFS.Hooks.GitCommandResolver.InvalidFileAttributes,
                out bool shouldLock,
                out string error).ShouldBeFalse();

            shouldLock.ShouldBeTrue();
            error.ShouldContain("exit code 128", "fatal: bad config");
        }

        private static string[] CreatePreCommandArgs(string command)
        {
            return new[] { "pre-command" }.Concat(command.Split(' ')).Concat(new[] { "--git-pid=456" }).ToArray();
        }

        private static void AssertLockPolicy(string command, bool shouldLock)
        {
            // The hook sends post-command for git-<name> to the lock-ownership check, not to this policy.
            string[] hooks = command.StartsWith("git-", System.StringComparison.Ordinal)
                ? new[] { "pre-command" }
                : new[] { "pre-command", "post-command" };
            foreach (string hook in hooks)
            {
                string[] args = new[] { hook }
                    .Concat(command.Split(' '))
                    .Concat(hook == "post-command"
                        ? new[] { "--git-pid=456", "--exit_code=0" }
                        : new[] { "--git-pid=456" })
                    .ToArray();

                GVFS.Hooks.Program.TryShouldLock(
                    args,
                    () => throw new AssertionException("Unexpected Git alias list for " + string.Join(" ", args)),
                    @"mock:\tools",
                    path => throw new AssertionException("Unexpected PATH search for " + path),
                    out bool actual,
                    out string error).ShouldBeTrue(error);

                actual.ShouldEqual(shouldLock, string.Join(" ", args));
            }
        }
    }
}
