using System;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConePatternSetTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void AcceptsDistinctDirectories(bool ignoreCase)
        {
            ConePatternSet cone = new ConePatternSet(new[] { "A", "A/B" }, new[] { "A/C", "\u00C4", "\u00E4" }, ignoreCase);

            cone.IgnoreCase.ShouldEqual(ignoreCase);
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A", "A/B");
            cone.RecursiveDirectories.ShouldMatchInOrder("A/C", "\u00C4", "\u00E4");
        }

        [TestCase]
        public void AcceptsAsciiCaseVariantsWhenCaseMatters()
        {
            new ConePatternSet(new[] { "Src", "src" }, new[] { "SRC" }, ignoreCase: false)
                .ParentOnlyDirectories.ShouldMatchInOrder("Src", "src");
        }

        // Git turns off cone matching for most of these. The set rejects all of them, so that
        // each directory has one meaning in the cone.
        [TestCase(new[] { "A", "A" }, new string[0], false)]
        [TestCase(new string[0], new[] { "A", "A" }, false)]
        [TestCase(new[] { "A" }, new[] { "A" }, false)]
        [TestCase(new[] { "Src", "src" }, new string[0], true)]
        [TestCase(new[] { "Src" }, new[] { "src" }, true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RejectsRepeatedDirectories(string[] parentOnly, string[] recursive, bool ignoreCase)
        {
            Assert.Throws<ArgumentException>(() => new ConePatternSet(parentOnly, recursive, ignoreCase));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("/A")]
        [TestCase("A/")]
        [TestCase("A//B")]
        [TestCase(".")]
        [TestCase("A/..")]
        [TestCase("A/./B")]
        [TestCase("*")]
        [TestCase("A/*")]
        [TestCase("A\nB")]
        [TestCase("A\rB")]
        [TestCase("A\0B")]
        [TestCase(@"A\B")]
        [TestCase(@"\")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RejectsDirectoriesThatGitCannotReadBack(string directory)
        {
            ConePatternSet.IsValidDirectory(directory).ShouldBeFalse();
            Assert.Throws<ArgumentException>(() => new ConePatternSet(new[] { directory }, new string[0], ignoreCase: false));
            Assert.Throws<ArgumentException>(() => new ConePatternSet(new string[0], new[] { directory }, ignoreCase: false));
        }

        [TestCase]
        public void RejectsUnpairedSurrogates()
        {
            // Attribute arguments cannot hold an unpaired surrogate, so build the strings here.
            ConePatternSet.IsValidDirectory(new string(new[] { 'A', '\uD800' })).ShouldBeFalse();
            ConePatternSet.IsValidDirectory(new string(new[] { '\uDC00', 'A' })).ShouldBeFalse();
            ConePatternSet.IsValidDirectory(new string(new[] { '\uD800', 'A', '\uDC00' })).ShouldBeFalse();
        }

        [TestCase("A")]
        [TestCase("A/B")]
        [TestCase("weird*dir")]
        [TestCase("br[1]")]
        [TestCase("...")]
        [TestCase("\u00C4/\uD83D\uDE00")]
        public void AcceptsOtherDirectories(string directory)
        {
            ConePatternSet.IsValidDirectory(directory).ShouldBeTrue();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RejectsNullLists()
        {
            Assert.Throws<ArgumentNullException>(() => new ConePatternSet(null, new string[0], ignoreCase: false));
            Assert.Throws<ArgumentNullException>(() => new ConePatternSet(new string[0], null, ignoreCase: false));
        }
    }
}
