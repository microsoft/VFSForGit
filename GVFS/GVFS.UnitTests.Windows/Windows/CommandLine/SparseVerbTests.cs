using GVFS.CommandLine;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Windows.Windows.CommandLine
{
    [TestFixture]
    public class SparseVerbTests
    {
        // Shorter name for readability
        private static char dirSep = Path.DirectorySeparatorChar;

        [TestCase]
        public void PathCoveredBySparseFolders_RootPaths()
        {
            string rootPaths = $"a.txt{SparseVerb.StatusPathSeparatorToken}b.txt{SparseVerb.StatusPathSeparatorToken}c.txt{SparseVerb.StatusPathSeparatorToken}";

            ConfirmAllPathsCovered(rootPaths, new HashSet<string>());
            ConfirmAllPathsCovered(rootPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });
            ConfirmAllPathsCovered(rootPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", $"B{dirSep}C" });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_RecursivelyCoveredPaths()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/a.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"A/B/B.txt{SparseVerb.StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });
            ConfirmAllPathsCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", $"B{dirSep}C" });

            // Root entries should always be covered
            rootPaths.Append($"d.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"e.txt{SparseVerb.StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });
            ConfirmAllPathsCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", $"B{dirSep}C" });

            rootPaths.Append($"B/C/e.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"B/C/F/g.txt{SparseVerb.StatusPathSeparatorToken}");
            ConfirmAllPathsCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", $"B{dirSep}C" });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_NonRecursivelyCoveredPaths()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/B/B.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"A/C.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"A/D/E/C.txt{SparseVerb.StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(
                rootPaths.ToString(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    $"A{dirSep}B{dirSep}C{dirSep}D",
                    $"A{dirSep}D{dirSep}E{dirSep}F{dirSep}G"
                });

            // Root entries should always be covered
            rootPaths.Append($"d.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"e.txt{SparseVerb.StatusPathSeparatorToken}");

            ConfirmAllPathsCovered(
                rootPaths.ToString(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    $"A{dirSep}B{dirSep}C{dirSep}D",
                    $"A{dirSep}D{dirSep}E{dirSep}F{dirSep}G"
                });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_PathsNotCovered()
        {
            StringBuilder rootPaths = new StringBuilder();
            rootPaths.Append($"A/B/B.txt{SparseVerb.StatusPathSeparatorToken}");
            rootPaths.Append($"A/D/E/C.txt{SparseVerb.StatusPathSeparatorToken}");

            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"A{dirSep}C" });
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B" });
            ConfirmAllPathsNotCovered(rootPaths.ToString(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B", $"C{dirSep}D" });
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
                int nextSeparatorIndex = paths.IndexOf(SparseVerb.StatusPathSeparatorToken, index);
                string expectedGitPath = paths.Substring(index, nextSeparatorIndex - index);
                SparseVerb.PathCoveredBySparseFolders(ref index, paths, sparseSet, out string gitPath).ShouldEqual(shouldBeCovered);
                index.ShouldEqual(nextSeparatorIndex + 1);
                gitPath.ShouldEqual(expectedGitPath);
            }
        }
    }
}
