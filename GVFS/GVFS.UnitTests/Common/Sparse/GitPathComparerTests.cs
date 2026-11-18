using System;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class GitPathComparerTests
    {
        [TestCase(false, "Src", "Src", true)]
        [TestCase(false, "Src", "src", false)]
        [TestCase(true, "Src", "src", true)]
        [TestCase(true, "SRC/Deep", "src/deep", true)]
        [TestCase(true, "\u00C4", "\u00E4", false)]
        [TestCase(true, "a", "ab", false)]
        [TestCase(true, "ab", "a", false)]
        [TestCase(true, null, "a", false)]
        [TestCase(true, "a", null, false)]
        [TestCase(true, null, null, true)]
        public void EqualsFoldsAsciiOnlyWhenIgnoringCase(bool ignoreCase, string left, string right, bool expected)
        {
            GitPathComparer comparer = GitPathComparer.GetComparer(ignoreCase);

            comparer.Equals(left, right).ShouldEqual(expected);
            if (expected && left != null)
            {
                comparer.GetHashCode(left).ShouldEqual(comparer.GetHashCode(right));
            }
        }

        [TestCase(false, "A/B", "A", true)]
        [TestCase(false, "A/B", "a", false)]
        [TestCase(true, "A/B", "a", true)]
        [TestCase(true, "AB", "a", false)]
        [TestCase(true, "A", "A", false)]
        [TestCase(true, "A", "A/B", false)]
        public void IsDescendantOfRequiresASeparatorAfterTheAncestor(bool ignoreCase, string path, string ancestor, bool expected)
        {
            GitPathComparer.GetComparer(ignoreCase).IsDescendantOf(path, ancestor).ShouldEqual(expected);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetHashCodeThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => GitPathComparer.IgnoreAsciiCase.GetHashCode(null));
        }
    }
}
