using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitPathConverterTests
    {
        private const string OctetEncoded = @"\330\261\331\212\331\204\331\214\330\243\331\203\330\252\331\210\330\250\330\261\303\273\331\205\330\247\330\261\330\263\330\243\330\272\330\263\330\267\330\263\302\272\331\260\331\260\333\202\331\227\331\222\333\265\330\261\331\212\331\204\331\214\330\243\331\203";
        private const string Utf8Encoded = @"ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك";
        private const string TestPath = @"/GVFS/";

        [TestCase]
        public void NullFilepathTest()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(null).ShouldEqual(null);
        }

        [TestCase]
        public void EmptyFilepathTest()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(string.Empty).ShouldEqual(string.Empty);
        }

        [TestCase]
        public void FilepathWithoutOctets()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(TestPath + "test.cs").ShouldEqual(TestPath + "test.cs");
        }

        [TestCase]
        public void FilepathWithoutOctetsAsFilename()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(TestPath + OctetEncoded).ShouldEqual(TestPath + Utf8Encoded);
        }

        [TestCase]
        public void FilepathWithoutOctetsAsFilenameNoExtension()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(TestPath + OctetEncoded + ".txt").ShouldEqual(TestPath + Utf8Encoded + ".txt");
        }

        [TestCase]
        public void FilepathWithoutOctetsAsFolder()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(TestPath + OctetEncoded + "/file.txt").ShouldEqual(TestPath + Utf8Encoded + "/file.txt");
        }

        [TestCase]
        public void FilepathWithoutOctetsAsFileAndFolder()
        {
            GitPathConverter.ConvertPathOctetsToUtf8(TestPath + OctetEncoded + TestPath + OctetEncoded + ".txt").ShouldEqual(TestPath + Utf8Encoded + TestPath + Utf8Encoded + ".txt");
        }
    }
}
