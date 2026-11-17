using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class SparseDirectoryExpansionGuardTests
    {
        [TestCase("A/B/f.txt", "A/", true)]
        [TestCase("A/f.txt", "A/", true)]
        [TestCase("A/", "A/", true)]
        [TestCase("B/f.txt", "A/", false)]
        [TestCase("AB/f.txt", "A/", false)]
        [TestCase("f.txt", "A/", false)]
        [TestCase("A/", "A/B/", true)]
        [TestCase("A/f.txt", "A/B/", false)]
        [TestCase("a/b/f.txt", "A/", true)]
        public void FindHiddenModifiedPathsFindsPathsInsideOrAroundASparseDirectory(string modifiedPath, string sparseDirectory, bool hidden)
        {
            List<string> result = SparseDirectoryExpansionGuard.FindHiddenModifiedPaths(new[] { modifiedPath }, new[] { sparseDirectory }, ignoreCase: true);

            (result.Count == 1).ShouldEqual(hidden);
        }

        [TestCase]
        public void FindHiddenModifiedPathsUsesTheCaseRule()
        {
            SparseDirectoryExpansionGuard.FindHiddenModifiedPaths(new[] { "a/f.txt" }, new[] { "A/" }, ignoreCase: false).ShouldBeEmpty();
        }

        [TestCase("A/B/b.txt", "A/B/deleted.txt", true)]
        [TestCase("A/B/", "A/B/C/deleted.txt", true)]
        [TestCase("A/B/b.txt", "A/X/deleted.txt", false)]
        [TestCase("A/B/b.txt", "A/deleted.txt", true)]
        [TestCase("A/b.txt", "A/B/", false)]
        [TestCase("A/B/C/c.txt", "A/B/", true)]
        [TestCase("A/", "A/B/", true)]
        [TestCase("C/c.txt", "A/B/deleted.txt", false)]
        public void FindReachedPathReturnsAPathWhoseDirectoryTheConeIncludes(string conePath, string unsafePath, bool reached)
        {
            ConePatternSet cone = ConeBuilder.BuildFromModifiedPaths(new[] { conePath }, ignoreCase: false);

            string result = SparseDirectoryExpansionGuard.FindReachedPath(cone, new[] { unsafePath }, ignoreCase: false);

            (result == unsafePath).ShouldEqual(reached);
            (result == null).ShouldEqual(!reached);
        }
    }
}
