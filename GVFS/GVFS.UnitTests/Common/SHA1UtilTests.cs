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
    }
}
