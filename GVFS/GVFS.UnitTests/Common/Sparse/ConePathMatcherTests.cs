using System;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConePathMatcherTests
    {
        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConstructorThrowsOnNullCone()
        {
            Assert.Throws<ArgumentNullException>(() => new ConePathMatcher(null));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void IsInConeThrowsOnNullPath()
        {
            ConePathMatcher matcher = new ConePathMatcher(Cone());
            Assert.Throws<ArgumentNullException>(() => matcher.IsInCone(null));
        }

        [TestCase("root.txt", true)]
        [TestCase("", true)]
        [TestCase("/", true)]
        [TestCase("X/f.txt", false)]
        [TestCase("X/", false)]
        [TestCase("A/f.txt", true)]
        [TestCase("A/", true)]
        [TestCase("A/C/f.txt", false)]
        [TestCase("A/C/", false)]
        [TestCase("A/B/f.txt", true)]
        [TestCase("A/B/", true)]
        [TestCase("A/B/E/f.txt", false)]
        [TestCase("A/D/f.txt", true)]
        [TestCase("A/D/E/F/f.txt", true)]
        [TestCase("A/D/E/F/", true)]
        [TestCase(@"A\D\E\f.txt", true)]
        [TestCase("/A/f.txt", true)]
        public void IsInConeFollowsConeRules(string path, bool expected)
        {
            // Parent-only A and A/B, recursive A/D.
            ConePathMatcher matcher = new ConePathMatcher(
                ConeBuilder.BuildFromModifiedPaths(new[] { "A/B/b.txt", "A/D/" }, ignoreCase: false));

            matcher.IsInCone(path).ShouldEqual(expected);
        }

        [TestCase]
        public void PathThatIsItselfRecursiveIsInCone()
        {
            // Git checks the full path against the recursive set before it checks the parent,
            // so the path is in the cone even when the cone omits the parent pair. For a
            // directory query, git checks the file "-" inside the directory the same way.
            ConePathMatcher matcher = new ConePathMatcher(Cone(recursive: new[] { "A/B", "C/-" }));

            matcher.IsInCone("A/B").ShouldBeTrue();
            matcher.IsInCone("A/B/f.txt").ShouldBeTrue();
            matcher.IsInCone("A/f.txt").ShouldBeFalse();
            matcher.IsInCone("C/").ShouldBeTrue();
            matcher.IsInCone("C/f.txt").ShouldBeFalse();
        }

        [TestCase(false, "a/f.txt", false)]
        [TestCase(true, "a/f.txt", true)]
        [TestCase(true, "a/c/f.txt", false)]
        [TestCase(true, "a/d/e/f.txt", true)]
        [TestCase(true, "a/D/", true)]
        [TestCase(false, "\u00C4/f.txt", true)]
        [TestCase(false, "\u00E4/f.txt", false)]
        [TestCase(true, "\u00E4/f.txt", false)]
        public void IgnoreCaseFoldsAsciiOnly(bool ignoreCase, string path, bool expected)
        {
            // The matcher uses the case rule of the cone. Git folds only ASCII letters under
            // core.ignoreCase (see GitPathComparer).
            ConePathMatcher matcher = new ConePathMatcher(
                Cone(parentOnly: new[] { "A", "\u00C4" }, recursive: new[] { "A/D" }, ignoreCase: ignoreCase));

            matcher.IsInCone(path).ShouldEqual(expected);
        }

        private static ConePatternSet Cone(string[] parentOnly = null, string[] recursive = null, bool ignoreCase = false)
        {
            return new ConePatternSet(parentOnly ?? new string[0], recursive ?? new string[0], ignoreCase);
        }
    }
}
