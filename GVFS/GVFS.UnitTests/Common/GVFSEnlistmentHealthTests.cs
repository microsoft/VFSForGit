using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSEnlistmentHealthTests
    {
        private readonly char sep = GVFSPlatform.GVFSPlatformConstants.PathSeparator;
        private GVFSEnlistmentHealthCalculator enlistmentHealthCalculator;
        private GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData enlistmentHealthData;

        [TestCase]
        public void SingleHydratedDirectoryShouldHaveOtherDirectoriesCompletelyHealthy()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddPlaceholderFiles("A/1.js", "A/2.js", "A/3.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(0.75m);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Key.ShouldEqual("B");
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Value.ShouldEqual(0);
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Key.ShouldEqual("C");
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
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);
        }

        [TestCase]
        public void OverHydrated()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddPlaceholderFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddModifiedPathFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(12);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(12);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(12);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void SortByHydration()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("C/1.js", "A/1.js", "B/1.js", "B/2.js", "A/2.js", "C/2.js", "A/3.js", "C/3.js", "B/3.js")
                .AddModifiedPathFiles("C/1.js", "B/2.js", "A/3.js")
                .AddPlaceholderFiles("B/1.js", "A/2.js")
                .AddModifiedPathFiles("A/1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(2);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(4);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Key.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Key.ShouldEqual("B");
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Key.ShouldEqual("C");
        }

        [TestCase]
        public void VariedDirectoryFormatting()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddPlaceholderFolders("/A/", $"{this.sep}B{this.sep}", "A/", $"B{this.sep}", "/A", $"{this.sep}B", "A", "B")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(8);

            // If the count of the sorted list is not 2, the different directory formats were considered distinct
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        [TestCase]
        public void VariedFilePathFormatting()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddPlaceholderFiles("A/1.js", $"A{this.sep}2.js", "/A/1.js", $"{this.sep}A{this.sep}1.js")
                .AddModifiedPathFiles("B/1.js", $"B{this.sep}2.js", "/B/1.js", $"{this.sep}B{this.sep}1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

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
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddPlaceholderFiles("A/1.js", $"A{this.sep}2.js", "/A/1.js", $"{this.sep}A{this.sep}1.js")
                .AddModifiedPathFiles("B/1.js", $"B{this.sep}2.js", "/B/1.js", $"{this.sep}B{this.sep}1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, "A/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(3);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(4);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(4.0m / 3.0m);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);

            this.enlistmentHealthData = this.GenerateStatistics(pathData, "/B/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(3);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(4);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(4.0m / 3.0m);
        }

        [TestCase]
        public void FilterByDirectoryWithoutPathSeparator()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("Directory1/Child1/File1.js", "Directory1/Child1/File2.exe", "Directory2/Child2/File1.bat", "Directory2/Child2/File2.css")
                .AddPlaceholderFiles("Directory1/File1.js", "Directory1/File2.exe", "Directory2/File1.bat", "Directory2/File2.css")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);

            this.enlistmentHealthData = this.GenerateStatistics(pathData, GVFSConstants.GitPathSeparatorString);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);

            this.enlistmentHealthData = this.GenerateStatistics(pathData, "Directory");
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
        }

        [TestCase]
        public void EnsureFolderNotIncludedInOwnCount()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFolders("foo/")
                .AddGitFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(1);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(5);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(6);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(5m / 6m);
        }

        [TestCase]
        public void FolderNotDoubleCounted()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFolders("foo/")
                .AddGitFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFolders("/foo")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Value.ShouldEqual(1);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(6);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(6);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void UnionOfSkipWorktreeAndModifiedPathsNoDuplicates()
        {
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddModifiedPathFiles("A/1.js", "A/2.js", "/A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddNonSkipWorktreeFiles("A/1.js", "/A/2.js", $"{this.sep}A/3.js", "B/1.js", $"B{this.sep}2.js", $"/B{this.sep}3.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(6);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        private GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData GenerateStatistics(GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData, string directory)
        {
            this.enlistmentHealthCalculator = new GVFSEnlistmentHealthCalculator(pathData);
            return this.enlistmentHealthCalculator.CalculateStatistics(directory);
        }

        public class PathDataBuilder
        {
            private GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData();

            public PathDataBuilder AddPlaceholderFiles(params string[] placeholderFilePaths)
            {
                this.pathData.PlaceholderFilePaths.AddRange(placeholderFilePaths);
                return this;
            }

            public PathDataBuilder AddPlaceholderFolders(params string[] placeholderFolderPaths)
            {
                this.pathData.PlaceholderFolderPaths.AddRange(placeholderFolderPaths);
                return this;
            }

            public PathDataBuilder AddModifiedPathFiles(params string[] modifiedFilePaths)
            {
                this.pathData.ModifiedFilePaths.AddRange(modifiedFilePaths);
                return this;
            }

            public PathDataBuilder AddModifiedPathFolders(params string[] modifiedFolderPaths)
            {
                this.pathData.ModifiedFolderPaths.AddRange(modifiedFolderPaths);
                return this;
            }

            public PathDataBuilder AddGitFiles(params string[] gitFilePaths)
            {
                this.pathData.GitFilePaths.AddRange(gitFilePaths);
                return this;
            }

            public PathDataBuilder AddGitFolders(params string[] gitFolderPaths)
            {
                this.pathData.GitFolderPaths.AddRange(gitFolderPaths);
                return this;
            }

            public PathDataBuilder AddNonSkipWorktreeFiles(params string[] nonSkipWorktreeFiles)
            {
                this.pathData.AddGitTrackingPaths(nonSkipWorktreeFiles);
                return this;
            }

            public GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData Build()
            {
                this.pathData.NormalizeAllPaths();
                return this.pathData;
            }
        }
    }
}
