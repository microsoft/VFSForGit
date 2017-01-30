using GVFS.GVFlt.DotGit;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    [TestFixture]
    public class GitConfigFileUtilsTests
    {
        [TestCase]
        public void SanitizeEmptyString()
        {
            string outputString;
            GitConfigFileUtils.TrySanitizeConfigFileLine(string.Empty, out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void SanitizePureWhiteSpace()
        {
            string outputString;
            GitConfigFileUtils.TrySanitizeConfigFileLine("   ", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine(" \t\t  ", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine(" \t\t\n\n  ", out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void SanitizeComment()
        {
            string outputString;
            GitConfigFileUtils.TrySanitizeConfigFileLine("# This is a comment ", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine("# This is a comment #", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine("## This is a comment ##", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine(" ## This is a comment ## ", out outputString).ShouldEqual(false);
            GitConfigFileUtils.TrySanitizeConfigFileLine("\t ## This is a comment ## \t ", out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void TrimWhitspace()
        {
            string outputString;
            GitConfigFileUtils.TrySanitizeConfigFileLine(" // ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("//");

            GitConfigFileUtils.TrySanitizeConfigFileLine(" /* ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/*");

            GitConfigFileUtils.TrySanitizeConfigFileLine(" /A ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigFileUtils.TrySanitizeConfigFileLine("\t /A \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigFileUtils.TrySanitizeConfigFileLine("  \t /A   \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");
        }

        [TestCase]
        public void TrimTrailingComment()
        {
            string outputString;
            GitConfigFileUtils.TrySanitizeConfigFileLine(" // # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("//");

            GitConfigFileUtils.TrySanitizeConfigFileLine(" /* # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/*");

            GitConfigFileUtils.TrySanitizeConfigFileLine(" /A # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigFileUtils.TrySanitizeConfigFileLine("\t /A \t # Trailing comment! \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigFileUtils.TrySanitizeConfigFileLine("  \t /A   \t # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");
        }
    }
}
