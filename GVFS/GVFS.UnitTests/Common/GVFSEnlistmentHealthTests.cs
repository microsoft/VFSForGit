using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSEnlistmentHealthTests
    {
        private List<string> fauxGitFiles = new List<string>();
        private List<string> fauxGitFolders = new List<string>();
        private List<string> fauxPlaceholderFiles = new List<string>();
        private List<string> fauxPlaceholderFolders = new List<string>();
        private List<string> fauxModifiedPathsFiles = new List<string>();
        private List<string> fauxModifiedPathsFolders = new List<string>();
        private List<string> fauxSkipWorktreeFiles = new List<string>();
        private GVFSEnlistmentHealthCalculator enlistmentHealthCalculator;
        private GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData enlistmentHealthData;

        [TestCase]
        public void OnlyOneHydratedDirectory()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(0.75m);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Value.ShouldEqual(0);
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Value.ShouldEqual(0);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(12);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(3);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0.25m);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);
        }

        [TestCase]
        public void AllEmptyLists()
        {
            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);
        }

        [TestCase]
        public void OverHydrated()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(12);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(12);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(12);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void SortByHydration()
        {
            this.fauxGitFiles.AddRange(new string[] { "C/1.js", "A/1.js", "B/1.js", "B/2.js", "A/2.js", "C/2.js", "A/3.js", "C/3.js", "B/3.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "C/1.js", "B/2.js", "A/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "B/1.js", "A/2.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "A/1.js" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(2);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(4);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Key.ShouldEqual("B");
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Key.ShouldEqual("C");
        }

        [TestCase]
        public void VariedDirectoryFormatting()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFolders.AddRange(new string[] { "/A/", @"\B\", "A/", @"B\", "/A", @"\B", "A", "B" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(8);

            // If the count of the sorted list is not 2, the different directory formats were considered distinct
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        [TestCase]
        public void VariedFilePathFormatting()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", @"A\2.js", "/A/1.js", @"\A\1.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "B/1.js", @"B\2.js", "/B/1.js", @"\B\1.js" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(4);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(4);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(6);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(2.0m / 3.0m);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(2.0m / 3.0m);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(4.0m / 3.0m);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Value.ShouldEqual(4.0m / 3.0m);
        }

        [TestCase]
        public void FilterByDirectory()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", @"A\2.js", "/A/1.js", @"\A\1.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "B/1.js", @"B\2.js", "/B/1.js", @"\B\1.js" });

            this.enlistmentHealthData = this.GenerateStatistics("A/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(3);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(4);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(4.0m / 3.0m);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("/");
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(1);

            this.enlistmentHealthData = this.GenerateStatistics("/B/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(3);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(4);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(4.0m / 3.0m);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("/");
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(1);
        }

        [TestCase]
        public void FilterByDirectoryWithoutPathSeparator()
        {
            this.fauxGitFiles.AddRange(new string[] { "Directory1/Child1/File1.js", "Directory1/Child1/File2.exe", "Directory2/Child2/File1.bat", "Directory2/Child2/File2.css" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "Directory1/File1.js", "Directory1/File2.exe", "Directory2/File1.bat", "Directory2/File2.css" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);

            this.enlistmentHealthData = this.GenerateStatistics("Directory");
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
        }

        [TestCase]
        public void EnsureFolderNotIncludedInOwnCount()
        {
            this.fauxGitFolders.Add("foo/");
            this.fauxGitFiles.AddRange(new string[] { "foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(1);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(5);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(6);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(5m / 6m);
        }

        [TestCase]
        public void FolderNotDoubleCounted()
        {
            this.fauxGitFolders.Add("foo/");
            this.fauxGitFiles.AddRange(new string[] { "foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg" });
            this.fauxPlaceholderFolders.Add("/foo");

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(1);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(6);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(6);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void UnionOfSkipWorktreeAndModifiedPathsNoDuplicates()
        {
            this.fauxGitFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "A/1.js", "A/2.js", "/A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxSkipWorktreeFiles.AddRange(new string[] { "A/1.js", "/A/2.js", "\\A/3.js", "B/1.js", "B\\2.js", "/B\\3.js" });

            this.enlistmentHealthData = this.GenerateStatistics(string.Empty);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(6);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        [SetUp]
        public void SetUpStructures()
        {
            this.fauxGitFiles.Clear();
            this.fauxGitFolders.Clear();
            this.fauxPlaceholderFiles.Clear();
            this.fauxPlaceholderFolders.Clear();
            this.fauxModifiedPathsFiles.Clear();
            this.fauxModifiedPathsFolders.Clear();
            this.fauxSkipWorktreeFiles.Clear();
        }

        private GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData GenerateStatistics(string directory)
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData();

            pathData.GitFolderPaths.AddRange(this.fauxGitFolders);
            pathData.GitFilePaths.AddRange(this.fauxGitFiles);
            pathData.PlaceholderFolderPaths.AddRange(this.fauxPlaceholderFolders);
            pathData.PlaceholderFilePaths.AddRange(this.fauxPlaceholderFiles);
            pathData.ModifiedFolderPaths.AddRange(this.fauxModifiedPathsFolders);
            pathData.ModifiedFilePaths.AddRange(this.fauxModifiedPathsFiles);
            pathData.AddSkipWorkTreeFilePaths(this.fauxSkipWorktreeFiles);

            pathData.NormalizeAllPaths();

            this.enlistmentHealthCalculator = new GVFSEnlistmentHealthCalculator(pathData);
            return this.enlistmentHealthCalculator.CalculateStatistics(directory);
        }
    }
}
