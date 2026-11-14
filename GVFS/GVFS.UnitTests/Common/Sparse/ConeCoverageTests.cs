using System.Collections.Generic;
using System.Linq;
using System.Text;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConeCoverageTests
    {
        private const string Header = "/*\n!/*/\n";

        [TestCase]
        public void ParseReadsBackWhatTheWriterWrites()
        {
            ConePatternSet written = new ConePatternSet(
                new[] { "A", "A/B", "W" },
                new[] { "A/B/C", "W/x*y", "Q[1]" },
                ignoreCase: false);

            ConeCoverage.TryParseConeFile(ConeFileWriter.Serialize(written), ignoreCase: false, out ConePatternSet read).ShouldBeTrue();

            read.ParentOnlyDirectories.ShouldMatchInOrder("A", "A/B", "W");
            read.RecursiveDirectories.ShouldMatchInOrder("A/B/C", "W/x*y", "Q[1]");
            read.IgnoreCase.ShouldBeFalse();
        }

        [TestCase]
        public void ParseAcceptsCrLfLineEndings()
        {
            ConeCoverage.TryParseConeFile("/*\r\n!/*/\r\n/A/\r\n", ignoreCase: true, out ConePatternSet read).ShouldBeTrue();
            read.RecursiveDirectories.ShouldMatchInOrder("A");
            read.IgnoreCase.ShouldBeTrue();
        }

        [TestCase]
        public void ParseAcceptsHeaderOnly()
        {
            ConeCoverage.TryParseConeFile(Header, ignoreCase: false, out ConePatternSet read).ShouldBeTrue();
            read.ParentOnlyDirectories.ShouldBeEmpty();
            read.RecursiveDirectories.ShouldBeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("/A/\n")]
        [TestCase("/*\n/A/\n")]
        [TestCase("!/*/\n/*\n/A/\n")]
        [TestCase("/A/\n/*\n!/*/\n")]
        [TestCase(Header + "!/A/*/\n")]
        [TestCase(Header + "!/A/*/\n/A/\n")]
        [TestCase(Header + "/A/\n!/A/*/\n!/A/*/\n")]
        [TestCase(Header + "/A/\n!/A/*/\n/A/\n")]
        [TestCase(Header + "/A*/\n")]
        [TestCase(Header + "/A\\b/\n")]
        [TestCase(Header + "/A\uFFFD/\n")]
        [TestCase(Header + "/A/\n/A/\n")]
        [TestCase(Header + "/A/./B/\n")]
        [TestCase(Header + "/A\n")]
        [TestCase(Header + "!/A/\n")]
        public void ParseRejectsFilesThatGitDoesNotReadAsTheSameCone(string content)
        {
            ConeCoverage.TryParseConeFile(content, ignoreCase: false, out ConePatternSet read).ShouldBeFalse();
            read.ShouldBeNull();
        }

        [TestCase]
        public void ParseAcceptsDirectoriesWithoutTheirAncestors()
        {
            // Git's reader does not add the ancestors of a cone directory, and neither does
            // ConePathMatcher, so both read this file with the same meaning.
            ConeCoverage.TryParseConeFile(Header + "/A/B/\n/C/\n!/C/*/\n/C/D/E/\n", ignoreCase: false, out ConePatternSet read).ShouldBeTrue();
            read.ParentOnlyDirectories.ShouldMatchInOrder("C");
            read.RecursiveDirectories.ShouldMatchInOrder("A/B", "C/D/E");

            ConeCoverage coverage = new ConeCoverage(read);
            coverage.IsCovered("A/B/file.txt").ShouldBeTrue();
            coverage.IsCovered("A/file.txt").ShouldBeFalse();
        }

        [TestCase("/*\n!/*/\n", true)]
        [TestCase("\n/*\r\n\r\n!/*/\r\n/A/\n", true)]
        [TestCase("# written by a tool\n/*\n# root files\n!/*/\n", true)]
        [TestCase("/.gitattributes\r\n", false)]
        [TestCase("!/*/\n/*\n", false)]
        [TestCase("/*\n/A/\n!/*/\n", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void HasConeHeaderNeedsTheHeaderLinesFirst(string content, bool expected)
        {
            ConeCoverage.HasConeHeader(content).ShouldEqual(expected);
        }

        [TestCase]
        public void ParseSkipsCommentLinesAsGitDoes()
        {
            ConeCoverage.TryParseConeFile("# note\n" + Header + "# A is recursive\n/A/\n", ignoreCase: false, out ConePatternSet read).ShouldBeTrue();
            read.RecursiveDirectories.ShouldMatchInOrder("A");
        }

        [TestCase]
        public void DecodeSkipsOnlyAUtf8ByteOrderMark()
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(Header + "/Ä/\n");
            byte[] utf8WithBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(utf8).ToArray();
            ConeCoverage.DecodeConeFile(utf8WithBom).ShouldEqual(Header + "/Ä/\n");
            ConeCoverage.DecodeConeFile(utf8).ShouldEqual(Header + "/Ä/\n");

            byte[] utf16WithBom = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Header + "/A/\n")).ToArray();
            ConeCoverage.HasConeHeader(ConeCoverage.DecodeConeFile(utf16WithBom)).ShouldBeFalse();
        }

        [TestCase]
        public void ParseAppliesTheCaseRule()
        {
            string content = Header + "/A/\n/a/\n";

            ConeCoverage.TryParseConeFile(content, ignoreCase: false, out ConePatternSet caseSensitive).ShouldBeTrue();
            caseSensitive.RecursiveDirectories.ShouldMatchInOrder("A", "a");

            ConeCoverage.TryParseConeFile(content, ignoreCase: true, out ConePatternSet caseInsensitive).ShouldBeFalse();
            caseInsensitive.ShouldBeNull();
        }

        [TestCase("root.txt", true)]
        [TestCase("A/file.txt", true)]
        [TestCase("A/Other/file.txt", false)]
        [TestCase("A/B/file.txt", true)]
        [TestCase("A/B/deep/er/file.txt", true)]
        [TestCase("Z/file.txt", false)]
        [TestCase("A/B/", true)]
        [TestCase("A/B/deep/", true)]
        [TestCase("A/", false)]
        [TestCase("A/Other/", false)]
        [TestCase("Z/", false)]
        public void IsCoveredFollowsTheCone(string path, bool expected)
        {
            CreateCoverage(ignoreCase: false).IsCovered(path).ShouldEqual(expected);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void IsCoveredUsesTheCaseRule(bool ignoreCase, bool expected)
        {
            ConeCoverage coverage = CreateCoverage(ignoreCase);
            coverage.IsCovered("a/b/file.txt").ShouldEqual(expected);
            coverage.IsCovered("a/b/sub/").ShouldEqual(expected);
        }

        [TestCase]
        public void GetUncoveredPathsKeepsInputOrder()
        {
            CreateCoverage(ignoreCase: false)
                .GetUncoveredPaths(new[] { "Z/z.txt", "A/a.txt", "Y/", "A/B/b.txt", "A/" })
                .ShouldMatchInOrder("Z/z.txt", "Y/", "A/");
        }

        private static ConeCoverage CreateCoverage(bool ignoreCase)
        {
            return new ConeCoverage(new ConePatternSet(new[] { "A" }, new[] { "A/B" }, ignoreCase));
        }
    }
}
