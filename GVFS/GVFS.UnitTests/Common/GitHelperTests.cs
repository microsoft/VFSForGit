using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitHelperTests
    {
        [TestCase]
        public void IsVerbTest()
        {
            GitHelper.IsVerb("git status --no-idea", "status").ShouldEqual(true);
            GitHelper.IsVerb("git status", "status").ShouldEqual(true);
            GitHelper.IsVerb("git statuses --no-idea", "status").ShouldEqual(false);
            GitHelper.IsVerb("git statuses", "status").ShouldEqual(false);

            GitHelper.IsVerb("git add some/file/to/add", "add", "status", "reset").ShouldEqual(true);
            GitHelper.IsVerb("git adding add", "add", "status", "reset").ShouldEqual(false);
            GitHelper.IsVerb("git add some/file/to/add", "adding", "status", "reset").ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHAIsFalseForEmptyString()
        {
            GitHelper.IsValidFullSHA(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHAIsFalseForHexStringsNot40Chars()
        {
            GitHelper.IsValidFullSHA("1").ShouldEqual(false);
            GitHelper.IsValidFullSHA("9").ShouldEqual(false);
            GitHelper.IsValidFullSHA("A").ShouldEqual(false);
            GitHelper.IsValidFullSHA("a").ShouldEqual(false);
            GitHelper.IsValidFullSHA("f").ShouldEqual(false);
            GitHelper.IsValidFullSHA("f").ShouldEqual(false);
            GitHelper.IsValidFullSHA("1234567890abcdefABCDEF").ShouldEqual(false);
            GitHelper.IsValidFullSHA("12345678901234567890123456789012345678901").ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHAFalseForNonHexStrings()
        {
            GitHelper.IsValidFullSHA("@").ShouldEqual(false);
            GitHelper.IsValidFullSHA("g").ShouldEqual(false);
            GitHelper.IsValidFullSHA("G").ShouldEqual(false);
            GitHelper.IsValidFullSHA("~").ShouldEqual(false);
            GitHelper.IsValidFullSHA("_").ShouldEqual(false);
            GitHelper.IsValidFullSHA(".").ShouldEqual(false);
            GitHelper.IsValidFullSHA("1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            GitHelper.IsValidFullSHA("G1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            GitHelper.IsValidFullSHA("_G1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            GitHelper.IsValidFullSHA("@234567890123456789012345678901234567890").ShouldEqual(false);
            GitHelper.IsValidFullSHA("g234567890123456789012345678901234567890").ShouldEqual(false);
            GitHelper.IsValidFullSHA("G234567890123456789012345678901234567890").ShouldEqual(false);
            GitHelper.IsValidFullSHA("~234567890123456789012345678901234567890").ShouldEqual(false);
            GitHelper.IsValidFullSHA("_234567890123456789012345678901234567890").ShouldEqual(false);
            GitHelper.IsValidFullSHA(".234567890123456789012345678901234567890").ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHATrueForLength40HexStrings()
        {
            GitHelper.IsValidFullSHA("1234567890123456789012345678901234567890").ShouldEqual(true);
            GitHelper.IsValidFullSHA("abcdef7890123456789012345678901234567890").ShouldEqual(true);
            GitHelper.IsValidFullSHA("ABCDEF7890123456789012345678901234567890").ShouldEqual(true);
            GitHelper.IsValidFullSHA("1234567890123456789012345678901234ABCDEF").ShouldEqual(true);
            GitHelper.IsValidFullSHA("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").ShouldEqual(true);
            GitHelper.IsValidFullSHA("ffffffffffffffffffffffffffffffffffffffff").ShouldEqual(true);
        }
    }
}
