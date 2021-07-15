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
        public void GetNextGitPathGetsPaths()
        {
            string testStatusOutput = $"a.txt{StatusPathSeparatorToken}";
            ConfirmGitPathsParsed(testStatusOutput, new List<string>() { "a.txt" });

            testStatusOutput = $"a.txt{StatusPathSeparatorToken}b.txt{StatusPathSeparatorToken}c.txt{StatusPathSeparatorToken}";
            ConfirmGitPathsParsed(testStatusOutput, new List<string>() { "a.txt", "b.txt", "c.txt" });

            testStatusOutput = $"a.txt{StatusPathSeparatorToken}d/b.txt{StatusPathSeparatorToken}d/c.txt{StatusPathSeparatorToken}";
            ConfirmGitPathsParsed(testStatusOutput, new List<string>() { "a.txt", "d/b.txt", "d/c.txt" });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_RootPaths()
        {
            List<string> testPaths = new List<string>()
            {
                "a.txt",
                "b.txt",
                "c.txt"
            };

            ConfirmAllPathsCovered(testPaths, EmptySparseSet);
            ConfirmAllPathsCovered(testPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });
            ConfirmAllPathsCovered(testPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", @"B\C" });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_RecursivelyCoveredPaths()
        {
            List<string> testPaths = new List<string>()
            {
                "A/a.txt",
                "A/B/B.txt"
            };

            HashSet<string> singleFolderSparseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
            HashSet<string> twoFolderSparseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", @"B\C" };

            ConfirmAllPathsCovered(testPaths, singleFolderSparseSet);
            ConfirmAllPathsCovered(testPaths, twoFolderSparseSet);

            // Root entries should always be covered
            testPaths.Add("d.txt");
            testPaths.Add("e.txt");
            ConfirmAllPathsCovered(testPaths, singleFolderSparseSet);
            ConfirmAllPathsCovered(testPaths, twoFolderSparseSet);

            testPaths.Add("B/C/e.txt");
            testPaths.Add("B/C/F/g.txt");
            ConfirmAllPathsCovered(testPaths, twoFolderSparseSet);
        }

        [TestCase]
        public void PathCoveredBySparseFolders_NonRecursivelyCoveredPaths()
        {
            List<string> testPaths = new List<string>()
            {
                "A/B/B.txt",
                "A/C.txt",
                "A/D/E/C.txt"
            };

            ConfirmAllPathsCovered(
                testPaths,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"A\B\C\D",
                    @"A\D\E\F\G"
                });

            // Root entries should always be covered
            testPaths.Add("d.txt");
            testPaths.Add("e.txt");

            ConfirmAllPathsCovered(
                testPaths,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"A\B\C\D",
                    @"A\D\E\F\G"
                });
        }

        [TestCase]
        public void PathCoveredBySparseFolders_PathsNotCovered()
        {
            List<string> testPaths = new List<string>()
            {
                "A/B/B.txt",
                "A/D/E/C.txt"
            };

            ConfirmAllPathsNotCovered(testPaths, EmptySparseSet);
            ConfirmAllPathsNotCovered(testPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"A\C" });
            ConfirmAllPathsNotCovered(testPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B" });
            ConfirmAllPathsNotCovered(testPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B", @"C\D" });
        }

        private static void ConfirmAllPathsCovered(List<string> paths, HashSet<string> sparseSet)
        {
            CheckIfPathsCovered(paths, sparseSet, shouldBeCovered: true);
        }

        private static void ConfirmAllPathsNotCovered(List<string> paths, HashSet<string> sparseSet)
        {
            CheckIfPathsCovered(paths, sparseSet, shouldBeCovered: false);
        }

        private static void ConfirmGitPathsParsed(string paths, List<string> expectedPaths)
        {
            int index = 0;
            int listIndex = 0;
            while (index < paths.Length - 1)
            {
                int nextSeparatorIndex = paths.IndexOf(StatusPathSeparatorToken, index);
                string expectedGitPath = expectedPaths[listIndex];
                SparseVerb.GetNextGitPath(ref index, paths).ShouldEqual(expectedGitPath);
                index.ShouldEqual(nextSeparatorIndex + 1);
                ++listIndex;
            }
        }

        private static void CheckIfPathsCovered(List<string> paths, HashSet<string> sparseSet, bool shouldBeCovered)
        {
            foreach (string path in paths)
            {
                SparseVerb.PathCoveredBySparseFolders(path, sparseSet).ShouldEqual(shouldBeCovered);
            }
        }
    }
}
