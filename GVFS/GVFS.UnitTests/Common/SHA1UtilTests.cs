using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Text;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class SHA1UtilTests
    {
        private const string TestString = "c:\\Repos\\GVFS\\src\\.gittattributes";
        private const string TestResultSha1 = "ced5ad9680c1a05e9100680c2b3432de23bb7d6d";
        private const string TestResultHex = "633a5c5265706f735c475646535c7372635c2e6769747461747472696275746573";

        [TestCase]
        public void SHA1HashStringForUTF8String()
        {
            SHA1Util.SHA1HashStringForUTF8String(TestString).ShouldEqual(TestResultSha1);
        }

        [TestCase]
        public void HexStringFromBytes()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(TestString);
            SHA1Util.HexStringFromBytes(bytes).ShouldEqual(TestResultHex);
        }

        [TestCase]
        public void IsValidFullSHAIsFalseForEmptyString()
        {
            SHA1Util.IsValidShaFormat(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHAIsFalseForHexStringsNot40Chars()
        {
            SHA1Util.IsValidShaFormat("1").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("9").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("A").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("a").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("f").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("f").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("1234567890abcdefABCDEF").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("12345678901234567890123456789012345678901").ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHAFalseForNonHexStrings()
        {
            SHA1Util.IsValidShaFormat("@").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("g").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("G").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("~").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("_").ShouldEqual(false);
            SHA1Util.IsValidShaFormat(".").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("G1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("_G1234567890abcdefABCDEF.tmp").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("@234567890123456789012345678901234567890").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("g234567890123456789012345678901234567890").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("G234567890123456789012345678901234567890").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("~234567890123456789012345678901234567890").ShouldEqual(false);
            SHA1Util.IsValidShaFormat("_234567890123456789012345678901234567890").ShouldEqual(false);
            SHA1Util.IsValidShaFormat(".234567890123456789012345678901234567890").ShouldEqual(false);
        }

        [TestCase]
        public void IsValidFullSHATrueForLength40HexStrings()
        {
            SHA1Util.IsValidShaFormat("1234567890123456789012345678901234567890").ShouldEqual(true);
            SHA1Util.IsValidShaFormat("abcdef7890123456789012345678901234567890").ShouldEqual(true);
            SHA1Util.IsValidShaFormat("ABCDEF7890123456789012345678901234567890").ShouldEqual(true);
            SHA1Util.IsValidShaFormat("1234567890123456789012345678901234ABCDEF").ShouldEqual(true);
            SHA1Util.IsValidShaFormat("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").ShouldEqual(true);
            SHA1Util.IsValidShaFormat("ffffffffffffffffffffffffffffffffffffffff").ShouldEqual(true);
        }
    }
}
