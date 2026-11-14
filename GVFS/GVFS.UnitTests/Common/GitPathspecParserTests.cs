using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitPathspecParserTests
    {
        // ── Command extraction ──────────────────────────────────────────

        [TestCase]
        public void EmptyArgs_ReturnsEmpty()
        {
            GitPathspecParser.ParseHookArgs(new string[0]).Command.ShouldBeNull();
            GitPathspecParser.ParseHookArgs(null).Command.ShouldBeNull();
            GitPathspecParser.ParseHookArgs(new[] { "pre-command" }).Command.ShouldBeNull();
        }

        [TestCase]
        public void Command_IsLowercasedAndGitPrefixStripped()
        {
            GitPathspecParser.ParseHookArgs(new[] { "pre-command", "ADD", "file.txt" })
                .Command.ShouldEqual("add");
            GitPathspecParser.ParseHookArgs(new[] { "pre-command", "git-add", "file.txt" })
                .Command.ShouldEqual("add");
        }

        // ── add / stage / rm / mv: all positionals are pathspecs ────────

        [TestCase]
        public void Add_InlinePathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "a.txt", "b.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt", "b.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Add_NoPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add" });
            result.Pathspecs.ShouldBeEmpty();
            result.NamesPaths.ShouldBeFalse();
        }

        [TestCase]
        public void Add_SkipsOptionFlags()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--all", "--verbose", "src/" });
            result.Pathspecs.ShouldMatchInOrder("src/");
        }

        [TestCase]
        public void Add_CombinedShortFlags_AreNotPathspecs()
        {
            // -Av = --all --verbose; neither takes a value, so "file" is a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "-Av", "file" });
            result.Pathspecs.ShouldMatchInOrder("file");
        }

        // ── Paths with spaces (shell already stripped quotes) ───────────

        [TestCase]
        public void Add_PathWithSpaces()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "my documents/todo list.txt" });
            result.Pathspecs.ShouldMatchInOrder("my documents/todo list.txt");
        }

        // ── "--" separator ──────────────────────────────────────────────

        [TestCase]
        public void Add_PathsAfterDashDash()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--", "-weird-name.txt", "b.txt" });
            result.Pathspecs.ShouldMatchInOrder("-weird-name.txt", "b.txt");
        }

        [TestCase]
        public void DashDash_TokensAreLiteralNotOptions()
        {
            // After --, a token that looks like an option is still a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--", "--all" });
            result.Pathspecs.ShouldMatchInOrder("--all");
        }

        // ── checkout: leading ref, then pathspecs ───────────────────────

        [TestCase]
        public void Checkout_BranchOnly_NoPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "my-branch" });
            result.Pathspecs.ShouldBeEmpty();
        }

        [TestCase]
        public void Checkout_HeadDashDashPaths()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "HEAD", "--", "foo.txt", "bar.txt" });
            result.Pathspecs.ShouldMatchInOrder("foo.txt", "bar.txt");
        }

        [TestCase]
        public void Checkout_RefThenPathspecsWithoutDashDash()
        {
            // git checkout branch file1 file2 — first positional is the ref.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "branch", "file1", "file2" });
            result.Pathspecs.ShouldMatchInOrder("file1", "file2");
        }

        [TestCase]
        public void Checkout_DashB_ConsumesBranchName_NotPathspec()
        {
            // -b <newbranch> creates a branch; the value is not a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "-b", "topic" });
            result.Pathspecs.ShouldBeEmpty();
        }

        [TestCase]
        public void Checkout_DashDashPathsAfterDashB()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "-b", "topic", "--", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        // ── reset: leading ref, then pathspecs ──────────────────────────

        [TestCase]
        public void Reset_MixedWithPaths()
        {
            // git reset HEAD file.txt — first positional is the ref.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "reset", "HEAD", "out1/f.txt" });
            result.Pathspecs.ShouldMatchInOrder("out1/f.txt");
        }

        [TestCase]
        public void Reset_PathsAfterDashDash()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "reset", "--", "out1/f.txt" });
            result.Pathspecs.ShouldMatchInOrder("out1/f.txt");
        }

        // ── restore: existing behavior must be covered ──────────────────

        [TestCase]
        public void Restore_StagedAllFiles()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--staged", "." });
            result.Pathspecs.ShouldMatchInOrder(".");
        }

        [TestCase]
        public void Restore_SkipsSourceSeparateValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--staged", "--source", "HEAD~1", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Restore_SkipsSourceEqualsValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--staged", "--source=HEAD~1", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Restore_SkipsShortSourceSeparateValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--staged", "-s", "HEAD~1", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        // ── commit: -m must not swallow a pathspec ──────────────────────

        [TestCase]
        public void Commit_ShortMessageSeparateValue_ThenPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "commit", "-m", "my message", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Commit_CombinedShortMessage_BakedValue()
        {
            // -am "msg": -a is boolean, -m is value-taking and last, so it
            // consumes "wip"; "file.txt" remains a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "commit", "-am", "wip", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Commit_ShortMessageBakedIntoCluster()
        {
            // -mwip: -m is value-taking but not last, so "wip" is its baked
            // value; "file.txt" is a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "commit", "-mwip", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Commit_LongMessageEqualsValue_ThenPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "commit", "--message=hello world", "file.txt" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        // ── switch: no pathspecs at all ─────────────────────────────────

        [TestCase]
        public void Switch_TakesNoPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "switch", "-c", "topic" });
            result.Pathspecs.ShouldBeEmpty();
            result.NamesPaths.ShouldBeFalse();
        }

        // ── stash push/save vs other subcommands ────────────────────────

        [TestCase]
        public void Stash_PushWithPaths()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "stash", "push", "out1/f.txt" });
            result.Pathspecs.ShouldMatchInOrder("out1/f.txt");
        }

        [TestCase]
        public void Stash_PushDashDashPaths()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "stash", "push", "--", "out1/f.txt", "out2/g.txt" });
            result.Pathspecs.ShouldMatchInOrder("out1/f.txt", "out2/g.txt");
        }

        [TestCase]
        public void Stash_PushSkipsMessageValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "stash", "push", "-m", "my stash", "out1/f.txt" });
            result.Pathspecs.ShouldMatchInOrder("out1/f.txt");
        }

        [TestCase]
        public void Stash_PopHasNoPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "stash", "pop" });
            result.Pathspecs.ShouldBeEmpty();
        }

        [TestCase]
        public void Stash_BareIsNotPush()
        {
            // Bare "git stash" is an implicit push but names no paths.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "stash" });
            result.Pathspecs.ShouldBeEmpty();
        }

        // ── diff / log / show: only paths after "--" count ──────────────

        [TestCase]
        public void Diff_RefsBeforeDashDashAreNotPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "diff", "main", "feature", "--", "src/file.cs" });
            result.Pathspecs.ShouldMatchInOrder("src/file.cs");
        }

        [TestCase]
        public void Log_NoDashDash_NoPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "log", "--oneline", "main" });
            result.Pathspecs.ShouldBeEmpty();
        }

        // ── Unknown command: only "--" paths count ──────────────────────

        [TestCase]
        public void UnknownCommand_OnlyDashDashPaths()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "frobnicate", "arg1", "--", "path.txt" });
            result.Pathspecs.ShouldMatchInOrder("path.txt");
        }

        // ── Injected --git-pid / --exit_code are filtered ───────────────

        [TestCase]
        public void GitPid_IsFiltered()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "file.txt", "--git-pid=1234" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void ExitCode_IsFilteredEvenPastDashDash()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "post-command", "add", "--", "file.txt", "--git-pid=1234", "--exit_code=0" });
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        // ── --pathspec-from-file / --pathspec-file-nul ──────────────────

        [TestCase]
        public void PathspecFromFile_EqualsForm()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--pathspec-from-file=list.txt" });
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.PathspecFileNul.ShouldBeFalse();
            result.Failed.ShouldBeFalse();
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void PathspecFromFile_SeparateArg()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--pathspec-from-file", "list.txt" });
            result.PathspecFromFile.ShouldEqual("list.txt");
        }

        [TestCase]
        public void PathspecFileNul_SetsFlag()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--pathspec-from-file=list.txt", "--pathspec-file-nul" });
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.PathspecFileNul.ShouldBeTrue();
        }

        [TestCase]
        public void PathspecFromFile_Stdin_Fails()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--pathspec-from-file=-" });
            result.Failed.ShouldBeTrue();
            result.NamesPaths.ShouldBeFalse();
        }

        [TestCase]
        public void PathspecFromFile_WithInlinePaths()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--pathspec-from-file=list.txt", "extra.txt" });
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.Pathspecs.ShouldMatchInOrder("extra.txt");
        }

        // ── Global options: -C, --git-dir, --work-tree ──────────────────

        [TestCase]
        public void Global_DashC_SeparateValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "-C", "sub/dir", "add", "file.txt" });
            result.ChangeDirectory.ShouldEqual("sub/dir");
            result.Command.ShouldEqual("add");
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Global_GitDir_SeparateValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "--git-dir", "/repo/.git", "add", "file.txt" });
            result.GitDir.ShouldEqual("/repo/.git");
            result.Command.ShouldEqual("add");
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Global_GitDir_EqualsValue()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "--git-dir=/repo/.git", "add", "file.txt" });
            result.GitDir.ShouldEqual("/repo/.git");
            result.Command.ShouldEqual("add");
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Global_WorkTree_AndConfig()
        {
            // -c key=value is a value-taking global; --work-tree consumes a value.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "-c", "core.autocrlf=false", "--work-tree", "/wt", "add", "file.txt" });
            result.WorkTree.ShouldEqual("/wt");
            result.Command.ShouldEqual("add");
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        [TestCase]
        public void Global_BooleanPaginate_DoesNotConsumeCommand()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "--no-pager", "add", "file.txt" });
            result.Command.ShouldEqual("add");
            result.Pathspecs.ShouldMatchInOrder("file.txt");
        }

        // ── Mixed: everything at once ───────────────────────────────────

        [TestCase]
        public void Mixed_GlobalsOptionsValueOptionsPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[]
                {
                    "pre-command", "-C", "repo", "commit",
                    "--message", "a commit", "-a", "src/a.cs", "src/b.cs", "--git-pid=99",
                });
            result.ChangeDirectory.ShouldEqual("repo");
            result.Command.ShouldEqual("commit");
            result.Pathspecs.ShouldMatchInOrder("src/a.cs", "src/b.cs");
        }

        // ── Out-of-cone conflict resolution ─────────────────────────────
        //
        // When a rebase/merge conflicts on a file outside the sparse cone, the
        // user resolves it by naming that path: "git add <path>", "git rm
        // <path>", "git checkout --ours/--theirs <path>", "git restore
        // <path>". Each must yield the path so the pre-command hook can widen
        // the cone and let plain (non "--sparse") Git succeed.

        [TestCase]
        public void Add_OutOfConeConflictPath_NamesPath()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Rm_OutOfConeConflictPath_NamesPath()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "rm", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Restore_ConflictPathWithOurs_NamesPath()
        {
            // restore treats every positional as a pathspec, so --ours is just a
            // boolean flag; the path is still extracted.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--ours", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Checkout_DashDashConflictPath_NamesPath()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Reset_DashDashConflictPath_NamesPath()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "reset", "--", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        // ── checkout --ours / --theirs / -p suppress the leading ref ────
        //
        // "git checkout --ours <path>" has no ref operand, so the first
        // positional is a pathspec, not a branch. Without this, the parser
        // would misread the conflicted path as a ref and never widen for it.

        [TestCase]
        public void Checkout_Ours_FirstPositionalIsPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--ours", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
            result.NamesPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Checkout_Theirs_FirstPositionalIsPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--theirs", "a.txt", "b.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt", "b.txt");
        }

        [TestCase]
        public void Checkout_ShortPatch_FirstPositionalIsPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "-p", "out0005/f0001.txt" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
        }

        [TestCase]
        public void Checkout_Ours_FlagAfterPathIsStillSuppressing()
        {
            // Flag order is not fixed: the ref-suppressing flag is detected
            // across the whole command, so the path is a pathspec even when the
            // flag follows it.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "out0005/f0001.txt", "--theirs" });
            result.Pathspecs.ShouldMatchInOrder("out0005/f0001.txt");
        }

        [TestCase]
        public void Checkout_Ours_NoPath_NoPathspecs()
        {
            // "git checkout --ours" with no path still names nothing.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--ours" });
            result.Pathspecs.ShouldBeEmpty();
            result.NamesPaths.ShouldBeFalse();
        }

        [TestCase]
        public void Checkout_LiteralOursPathAfterDashDash_IsPathspec()
        {
            // After "--", "--ours" is a literal path, not a ref-suppressing flag;
            // the branch before "--" is still a ref.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "topic", "--", "--ours" });
            result.Pathspecs.ShouldMatchInOrder("--ours");
        }

        // ── --end-of-options ────────────────────────────────────────────

        [TestCase]
        public void EndOfOptions_DashTokenIsPositional()
        {
            // "-topic" is a ref after --end-of-options, so it does not count as a
            // short option cluster, and the next positional is a pathspec.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--end-of-options", "-topic", "a.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt");
        }

        [TestCase]
        public void EndOfOptions_DashTokenIsPathspecForPathCommands()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "add", "--end-of-options", "-weird.txt", "--all" });
            result.Pathspecs.ShouldMatchInOrder("-weird.txt", "--all");
        }

        [TestCase]
        public void EndOfOptions_DashDashStillSeparatesPathspecs()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "diff", "--end-of-options", "-topic", "--", "a.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt");
        }

        [TestCase]
        public void EndOfOptions_LaterRefSuppressingFlagIsARef()
        {
            // "--ours" after --end-of-options is a ref name, so the leading ref
            // operand is not suppressed.
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "checkout", "--end-of-options", "--ours", "a.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt");
        }

        // ── Other verbs in the table ────────────────────────────────────

        [TestCase("stage")]
        [TestCase("rm")]
        [TestCase("mv")]
        public void PathspecVerbs_EveryPositionalIsAPathspec(string verb)
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", verb, "-q", "a.txt", "dir/", "--git-pid=1" });
            result.Pathspecs.ShouldMatchInOrder("a.txt", "dir/");
        }

        [TestCase("grep")]
        [TestCase("blame")]
        [TestCase("show")]
        public void RefsOnlyVerbs_OnlyPathsAfterDashDash(string verb)
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", verb, "HEAD", "--stat", "a.txt", "--", "b.txt" });
            result.Pathspecs.ShouldMatchInOrder("b.txt");
        }

        [TestCase]
        public void Commit_FixupValueIsNotAPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "commit", "--fixup", "HEAD~1", "a.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt");
        }

        [TestCase]
        public void Restore_ConflictEqualsValue_ThenPathspec()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "restore", "--conflict=diff3", "a.txt" });
            result.Pathspecs.ShouldMatchInOrder("a.txt");
        }

        [TestCase]
        public void Switch_DashDashPathsAreIgnored()
        {
            ParsedGitCommand result = GitPathspecParser.ParseHookArgs(
                new[] { "pre-command", "switch", "-c", "topic", "--", "a.txt" });
            result.Pathspecs.ShouldBeEmpty();
            result.NamesPaths.ShouldBeFalse();
        }
    }
}
