using GVFS.Hooks;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class UnstageTests
    {
        // ── IsUnstageOperation ──────────────────────────────────────────

        [TestCase]
        public void IsUnstageOperation_RestoreStaged()
        {
            UnstageCommandParser.IsUnstageOperation(
                "restore",
                new[] { "pre-command", "restore", "--staged", "." })
                .ShouldBeTrue();
        }

        [TestCase]
        public void IsUnstageOperation_RestoreShortFlag()
        {
            UnstageCommandParser.IsUnstageOperation(
                "restore",
                new[] { "pre-command", "restore", "-S", "file.txt" })
                .ShouldBeTrue();
        }

        [TestCase]
        public void IsUnstageOperation_RestoreCombinedShortFlags()
        {
            // -WS means --worktree --staged
            UnstageCommandParser.IsUnstageOperation(
                "restore",
                new[] { "pre-command", "restore", "-WS", "file.txt" })
                .ShouldBeTrue();
        }

        [TestCase]
        public void IsUnstageOperation_RestoreLowerS_NotStaged()
        {
            // -s means --source, not --staged
            UnstageCommandParser.IsUnstageOperation(
                "restore",
                new[] { "pre-command", "restore", "-s", "HEAD~1", "file.txt" })
                .ShouldBeFalse();
        }

        [TestCase]
        public void IsUnstageOperation_RestoreWithoutStaged()
        {
            UnstageCommandParser.IsUnstageOperation(
                "restore",
                new[] { "pre-command", "restore", "file.txt" })
                .ShouldBeFalse();
        }

        [TestCase]
        public void IsUnstageOperation_CheckoutHeadDashDash()
        {
            UnstageCommandParser.IsUnstageOperation(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--", "file.txt" })
                .ShouldBeTrue();
        }

        [TestCase]
        public void IsUnstageOperation_CheckoutNoDashDash()
        {
            UnstageCommandParser.IsUnstageOperation(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "file.txt" })
                .ShouldBeFalse();
        }

        [TestCase]
        public void IsUnstageOperation_CheckoutBranchName()
        {
            UnstageCommandParser.IsUnstageOperation(
                "checkout",
                new[] { "pre-command", "checkout", "my-branch" })
                .ShouldBeFalse();
        }

        [TestCase]
        public void IsUnstageOperation_OtherCommand()
        {
            UnstageCommandParser.IsUnstageOperation(
                "status",
                new[] { "pre-command", "status" })
                .ShouldBeFalse();
        }

        // ── GetRestorePathspec: inline pathspecs ────────────────────────

        [TestCase]
        public void GetRestorePathspec_RestoreStagedAllFiles()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "." });
            result.Failed.ShouldBeFalse();
            result.InlinePathspecs.ShouldEqual(".");
            result.PathspecFromFile.ShouldBeNull();
        }

        [TestCase]
        public void GetRestorePathspec_RestoreStagedSpecificFiles()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "a.txt", "b.txt" });
            result.Failed.ShouldBeFalse();
            result.InlinePathspecs.ShouldEqual("a.txt\0b.txt");
        }

        [TestCase]
        public void GetRestorePathspec_RestoreStagedNoPathspec()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged" });
            result.Failed.ShouldBeFalse();
            result.InlinePathspecs.ShouldEqual(string.Empty);
            result.PathspecFromFile.ShouldBeNull();
        }

        [TestCase]
        public void GetRestorePathspec_RestoreSkipsSourceFlag()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--source", "HEAD~1", "file.txt" });
            result.InlinePathspecs.ShouldEqual("file.txt");
        }

        [TestCase]
        public void GetRestorePathspec_RestoreSkipsSourceEqualsFlag()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--source=HEAD~1", "file.txt" });
            result.InlinePathspecs.ShouldEqual("file.txt");
        }

        [TestCase]
        public void GetRestorePathspec_RestoreSkipsShortSourceFlag()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "-s", "HEAD~1", "file.txt" });
            result.InlinePathspecs.ShouldEqual("file.txt");
        }

        [TestCase]
        public void GetRestorePathspec_RestorePathsAfterDashDash()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--", "a.txt", "b.txt" });
            result.InlinePathspecs.ShouldEqual("a.txt\0b.txt");
        }

        [TestCase]
        public void GetRestorePathspec_RestoreSkipsGitPid()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--git-pid=1234", "file.txt" });
            result.InlinePathspecs.ShouldEqual("file.txt");
        }

        // ── Checkout tree-ish stripping ────────────────────────────────

        [TestCase]
        public void GetRestorePathspec_CheckoutStripsTreeish()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--", "foo.txt" });
            result.InlinePathspecs.ShouldEqual("foo.txt");
        }

        [TestCase]
        public void GetRestorePathspec_CheckoutStripsTreeishMultiplePaths()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--", "a.txt", "b.txt" });
            result.InlinePathspecs.ShouldEqual("a.txt\0b.txt");
        }

        [TestCase]
        public void GetRestorePathspec_CheckoutNoPaths()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--" });
            result.InlinePathspecs.ShouldEqual(string.Empty);
        }

        [TestCase]
        public void GetRestorePathspec_CheckoutTreeishNotIncludedAsPaths()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--", "file.txt" });
            result.InlinePathspecs.ShouldNotContain(false, "HEAD");
        }

        // ── --pathspec-from-file forwarding ───────────────────────────

        [TestCase]
        public void GetRestorePathspec_PathspecFromFileEqualsForm()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-from-file=list.txt" });
            result.Failed.ShouldBeFalse();
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.PathspecFileNul.ShouldBeFalse();
        }

        [TestCase]
        public void GetRestorePathspec_PathspecFromFileSeparateArg()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-from-file", "list.txt" });
            result.Failed.ShouldBeFalse();
            result.PathspecFromFile.ShouldEqual("list.txt");
        }

        [TestCase]
        public void GetRestorePathspec_PathspecFileNulSetsFlag()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-from-file=list.txt", "--pathspec-file-nul" });
            result.Failed.ShouldBeFalse();
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.PathspecFileNul.ShouldBeTrue();
        }

        [TestCase]
        public void GetRestorePathspec_PathspecFromFileStdinFails()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-from-file=-" });
            result.Failed.ShouldBeTrue();
        }

        [TestCase]
        public void GetRestorePathspec_CheckoutPathspecFromFile()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "checkout",
                new[] { "pre-command", "checkout", "HEAD", "--pathspec-from-file=list.txt", "--" });
            result.Failed.ShouldBeFalse();
            result.PathspecFromFile.ShouldEqual("list.txt");
        }

        [TestCase]
        public void GetRestorePathspec_PathspecFileNulAloneIsIgnored()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-file-nul", "file.txt" });
            result.InlinePathspecs.ShouldEqual("file.txt");
            result.PathspecFromFile.ShouldBeNull();
        }

        [TestCase]
        public void GetRestorePathspec_PathspecFromFileWithInlinePaths()
        {
            UnstageCommandParser.PathspecResult result = UnstageCommandParser.GetRestorePathspec(
                "restore",
                new[] { "pre-command", "restore", "--staged", "--pathspec-from-file=list.txt", "extra.txt" });
            result.Failed.ShouldBeFalse();
            result.PathspecFromFile.ShouldEqual("list.txt");
            result.InlinePathspecs.ShouldEqual("extra.txt");
        }
    }
}
