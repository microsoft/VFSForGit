using GVFS.CommandLine;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

// These tests are in GVFS.UnitTests.Windows because they rely on GVFS.Windows
// which is a .NET Framework project and only supported on Windows
namespace GVFS.UnitTests.Windows.Windows.CommandLine
{
    [TestFixture]
    public class SparseVerbTests
    {
        private const char StatusPathSeparatorToken = '\0';

        private static readonly HashSet<string> EmptySparseSet = new HashSet<string>();

        [TestCase]
        public void PathCoveredBySparseFolders_RootPaths()
        {
            string rootPaths = $"a.txt{StatusPathSeparatorToken}b.txt{StatusPathSeparatorToken}c.txt{StatusPathSeparatorToken}";

            ConfirmAllPathsCovered(rootPaths, EmptySparseSet);
            ConfirmAllPathsCovered(rootPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });
            ConfirmAllPathsCovered(rootPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", @"B\C" });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_RecursivelyCoveredPaths()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/a.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"A/B/B.txt{StatusPathSeparatorToken}");

            HashSet<string> singleFolderSparseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
            HashSet<string> twoFolderSparseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", @"B\C" };

            ConfirmAllPathsCovered(rootPaths.ToString(), singleFolderSparseSet);
            ConfirmAllPathsCovered(rootPaths.ToString(), twoFolderSparseSet);

            // Root entries should always be covered
            rootPaths.Append($"d.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"e.txt{StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(rootPaths.ToString(), singleFolderSparseSet);
            ConfirmAllPathsCovered(rootPaths.ToString(), twoFolderSparseSet);

            rootPaths.Append($"B/C/e.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"B/C/F/g.txt{StatusPathSeparatorToken}");
            ConfirmAllPathsCovered(rootPaths.ToString(), twoFolderSparseSet);
        }

        [TestCase]
        public void PathCoveredBySparseFolders_NonRecursivelyCoveredPaths()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/B/B.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"A/C.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"A/D/E/C.txt{StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(
                rootPaths.ToString(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"A\B\C\D",
                    @"A\D\E\F\G"
                });

            // Root entries should always be covered
            rootPaths.Append($"d.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"e.txt{StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(
                rootPaths.ToString(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"A\B\C\D",
                    @"A\D\E\F\G"
                });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_PathsNotCovered()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/B/B.txt{StatusPathSeparatorToken}");
            rootPaths.Append($"A/D/E/C.txt{StatusPathSeparatorToken}");

            ConfirmAllPathsNotCovered(rootPaths.ToString(), EmptySparseSet);
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"A\C" });
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B" });
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B", @"C\D" });
        }

        private static void ConfirmAllPathsCovered(string paths, HashSet<string> sparseSet)
        {
            CheckIfPathsCovered(paths, sparseSet, shouldBeCovered: true);
        }

        private static void ConfirmAllPathsNotCovered(string paths, HashSet<string> sparseSet)
        {
            CheckIfPathsCovered(paths, sparseSet, shouldBeCovered: false);
        }

        private static void CheckIfPathsCovered(string paths, HashSet<string> sparseSet, bool shouldBeCovered)
        {
            int index = 0;
            while (index < paths.Length - 1)
            {
                int nextSeparatorIndex = paths.IndexOf(StatusPathSeparatorToken, index);
                string expectedGitPath = paths.Substring(index, nextSeparatorIndex - index);
                SparseVerb.PathCoveredBySparseFolders(ref index, paths, sparseSet, out string gitPath).ShouldEqual(shouldBeCovered);
                index.ShouldEqual(nextSeparatorIndex + 1);
                gitPath.ShouldEqual(expectedGitPath);
            }
        }
    }
}
