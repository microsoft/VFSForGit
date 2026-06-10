using GVFS.Hooks;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class ResetCommandParserTests
    {
        [TestCase]
        public void Parse_ImplicitMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "HEAD~1" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("HEAD~1");
            result.HasPaths.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_ExplicitMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--mixed", "HEAD~1" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("HEAD~1");
            result.HasPaths.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_SoftNotMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--soft", "HEAD~1" });

            result.IsMixedReset.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_HardNotMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--hard", "HEAD~1" });

            result.IsMixedReset.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_MergeNotMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--merge", "HEAD~1" });

            result.IsMixedReset.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_KeepNotMixed()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--keep", "HEAD~1" });

            result.IsMixedReset.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_NoTarget_ResetToHead()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldBeNull();
            result.HasPaths.ShouldBeFalse();
        }

        [TestCase]
        public void Parse_PathBasedReset()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "HEAD", "--", "path/to/file.txt" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("HEAD");
            result.HasPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Parse_PathBasedResetWithoutDashDash()
        {
            // git reset HEAD path/to/file.txt (second positional = path)
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "HEAD", "path/to/file.txt" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("HEAD");
            result.HasPaths.ShouldBeTrue();
        }

        [TestCase]
        public void Parse_CommitSha()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "abc123def" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("abc123def");
        }

        [TestCase]
        public void Parse_GitPidArgIgnored()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--git-pid=12345", "HEAD~2" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldEqual("HEAD~2");
        }

        [TestCase]
        public void Parse_ExplicitMixedNoTarget()
        {
            ResetCommandParser.ResetParseResult result = ResetCommandParser.Parse(
                new[] { "pre-command", "reset", "--mixed" });

            result.IsMixedReset.ShouldBeTrue();
            result.TargetCommit.ShouldBeNull();
        }
    }
}
