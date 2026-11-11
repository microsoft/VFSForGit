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
        [TestCase("bundle create mock.bundle HEAD")]
        [TestCase("cat-file")]
        [TestCase("check-attr")]
        [TestCase("check-ignore")]
        [TestCase("check-mailmap")]
        [TestCase("check-ref-format --branch topic")]
        [TestCase("check-ref-format --normalize refs/heads/topic")]
        [TestCase("check-ref-format --refspec-pattern refs/heads/*")]
        [TestCase("cherry -v HEAD~2 HEAD HEAD~1")]
        [TestCase("column --mode=column")]
        [TestCase("commit-graph")]
        [TestCase("commit-tree HEAD^{tree}")]
        [TestCase("config")]
        [TestCase("count-objects")]
        [TestCase("count-objects -vH")]
        [TestCase("credential")]
        [TestCase("describe")]
        [TestCase("describe --tags --always --abbrev=7 HEAD")]
        [TestCase("describe --abbrev")]
        [TestCase("describe --abbrev 7")]
        [TestCase("describe --contains --all --long --first-parent --exact-match")]
        [TestCase("describe --match mock-* --exclude=excluded-* --candidates 5")]
        [TestCase("describe --match --dirty")]
        [TestCase("describe -- --dirty")]
        [TestCase("diff")]
        [TestCase("diff-files")]
        [TestCase("diff-index")]
        [TestCase("diff-pairs")]
        [TestCase("diff-tree")]
        [TestCase("difftool")]
        [TestCase("fetch")]
        [TestCase("fmt-merge-msg")]
        [TestCase("for-each-ref")]
        [TestCase("format-patch --stdout --base=auto HEAD~1")]
        [TestCase("format-rev HEAD")]
        [TestCase("get-tar-commit-id")]
        [TestCase("grep pattern")]
        [TestCase("grep -in -e pattern -- Readme.md")]
        [TestCase("grep -nepattern")]
        [TestCase("grep -e -O -f patterns --cached")]
        [TestCase("grep -e-O")]
        [TestCase("grep --context=2 --threads 1 --color=never pattern")]
        [TestCase("grep --color pattern")]
        [TestCase("grep -- --open-files-in-pager")]
        [TestCase("grep -- -O")]
        [TestCase("help")]
        [TestCase("hash-object")]
        [TestCase("index-pack")]
        [TestCase("last-modified HEAD -- Readme.md")]
        [TestCase("log")]
        [TestCase("ls-files")]
        [TestCase("ls-remote --symref origin")]
        [TestCase("ls-remote --get-url origin")]
        [TestCase("ls-tree")]
        [TestCase("merge-base")]
        [TestCase("merge-file -p ours base theirs")]
        [TestCase("merge-file --stdout --diff3 -L ours ours base theirs")]
        [TestCase("merge-file -qpLlabel ours base theirs")]
        [TestCase("merge-file -p -L --no-stdout ours base theirs")]
        [TestCase("merge-file --object-id HEAD:a HEAD:b HEAD:c")]
        [TestCase("merge-file --object-id --marker-size=7 --diff-algorithm histogram a b c")]
        [TestCase("merge-file --stdout -- ours base --no-stdout")]
        [TestCase("merge-tree --write-tree HEAD HEAD")]
        [TestCase("mktag")]
        [TestCase("mktree")]
        [TestCase("multi-pack-index")]
        [TestCase("name-rev")]
        [TestCase("notes merge --commit")]
        [TestCase("pack-objects")]
        [TestCase("pack-refs --all")]
        [TestCase("patch-id --stable")]
        [TestCase("pickaxe HEAD -- Readme.md")]
        [TestCase("push")]
        [TestCase("range-diff HEAD~2..HEAD~1 HEAD~1..HEAD")]
        [TestCase("reflog")]
        [TestCase("reflog show --all")]
        [TestCase("reflog list")]
        [TestCase("reflog exists HEAD")]
        [TestCase("reflog write refs/heads/topic old new message")]
        [TestCase("reflog delete --rewrite --updateref topic@{0}")]
        [TestCase("reflog drop --all --single-worktree")]
        [TestCase("reflog expire --stale-fix --rewrite --updateref --all")]
        [TestCase("refs list")]
        [TestCase("refs exists refs/heads/topic")]
        [TestCase("refs verify --strict")]
        [TestCase("refs optimize --all")]
        [TestCase("refs create refs/heads/topic HEAD")]
        [TestCase("refs delete refs/heads/topic")]
        [TestCase("refs update refs/heads/topic HEAD")]
        [TestCase("refs rename refs/heads/topic refs/heads/new-topic")]
        [TestCase("remote")]
        [TestCase("replace --edit HEAD")]
        [TestCase("reset --soft")]
        [TestCase("rev-list")]
        [TestCase("rev-parse")]
        [TestCase("shortlog -sn HEAD")]
        [TestCase("show")]
        [TestCase("show-branch --list")]
        [TestCase("show-branch --merge-base")]
        [TestCase("show-branch --reflog")]
        [TestCase("show-index")]
        [TestCase("show-ref")]
        [TestCase("stripspace")]
        [TestCase("submodule")]
        [TestCase("git-submodule")]
        [TestCase("symbolic-ref")]
        [TestCase("tag")]
        [TestCase("unpack-objects")]
        [TestCase("update-ref")]
        [TestCase("update-server-info")]
        [TestCase("url-parse https://example.invalid/repo")]
        [TestCase("var GIT_EDITOR")]
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
        [TestCase("describe --dirty")]
        [TestCase("describe --dirty=modified")]
        [TestCase("describe --abbrev --dirty")]
        [TestCase("describe --di")]
        [TestCase("describe --dir=modified")]
        [TestCase("describe --broken")]
        [TestCase("describe --br=broken")]
        [TestCase("describe --tags --match mock-* --broken")]
        [TestCase("describe --unknown-option")]
        [TestCase("describe --match")]
        [TestCase("describe --git-pid=123")]
        [TestCase("grep -O pattern")]
        [TestCase("grep --color -O pattern")]
        [TestCase("grep --cached=value pattern")]
        [TestCase("grep -Omock-pager pattern")]
        [TestCase("grep -inO pattern")]
        [TestCase("grep -nOmock-pager pattern")]
        [TestCase("grep --open pattern")]
        [TestCase("grep --open-files-in-pager=mock-pager pattern")]
        [TestCase("grep -e pattern --open-files-in-pager")]
        [TestCase("grep --unknown-option pattern")]
        [TestCase("grep -e")]
        [TestCase("grep --threads")]
        [TestCase("grep --exit_code=0 pattern")]
        [TestCase("merge")]
        [TestCase("merge-file ours base theirs")]
        [TestCase("merge-file -- -p base theirs")]
        [TestCase("merge-file -L -p ours base theirs")]
        [TestCase("merge-file -p --no-stdout ours base theirs")]
        [TestCase("merge-file --object-id --no-object-id ours base theirs")]
        [TestCase("merge-file -p --no-st ours base theirs")]
        [TestCase("merge-file --obj ours base theirs")]
        [TestCase("merge-file -p --unknown-option ours base theirs")]
        [TestCase("merge-file -p -L")]
        [TestCase("mv")]
        [TestCase("pull")]
        [TestCase("read-tree")]
        [TestCase("rebase")]
        [TestCase("refs")]
        [TestCase("refs migrate --ref-format=reftable")]
        [TestCase("refs unknown list")]
        [TestCase("refs --unknown-option list")]
        [TestCase("refs LIST")]
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

        [TestCase("describe")]
        [TestCase("describe --match value")]
        [TestCase("grep pattern")]
        [TestCase("grep --color pattern")]
        [TestCase("merge-file -p a b c")]
        [TestCase("merge-file --object-id a b c")]
        public void PartialExemptionsRequireSeparateHookMetadata(string command)
        {
            AssertLockPolicy(command, shouldLock: true, includeHookMetadata: false);
        }

        [TestCase("git-annotate")]
        [TestCase("git-DIFF")]
        [TestCase("git-LOG")]
        [TestCase("git-reflog")]
        [TestCase("git-merge-tree")]
        [TestCase("git-describe")]
        [TestCase("git-grep pattern")]
        [TestCase("git-merge-file -p ours base theirs")]
        [TestCase("git-refs list")]
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

        private static void AssertLockPolicy(string command, bool shouldLock, bool includeHookMetadata = true)
        {
            // The hook sends post-command for git-<name> to the lock-ownership check, not to this policy.
            string[] hooks = command.StartsWith("git-", System.StringComparison.Ordinal)
                ? new[] { "pre-command" }
                : new[] { "pre-command", "post-command" };
            foreach (string hook in hooks)
            {
                string[] args = new[] { hook }
                    .Concat(command.Split(' '))
                    .Concat(!includeHookMetadata
                        ? System.Array.Empty<string>()
                        : hook == "post-command"
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
