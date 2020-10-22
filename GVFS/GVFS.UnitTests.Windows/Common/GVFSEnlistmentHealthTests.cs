using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSEnlistmentHealthTests
    {
        private readonly char sep = GVFSPlatform.GVFSPlatformConstants.PathSeparator;
        private EnlistmentHealthCalculator enlistmentHealthCalculator;
        private EnlistmentHealthData enlistmentHealthData;

        [TestCase]
        public void SingleHydratedDirectoryShouldHaveOtherDirectoriesCompletelyHealthy()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddPlaceholderFiles("A/1.js", "A/2.js", "A/3.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.DirectoryHydrationLevels[0].Name.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[0].HydratedFileCount.ShouldEqual(3);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].TotalFileCount.ShouldEqual(4);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Name.ShouldEqual("B");
            this.enlistmentHealthData.DirectoryHydrationLevels[1].HydratedFileCount.ShouldEqual(0);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].TotalFileCount.ShouldEqual(4);
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Name.ShouldEqual("C");
            this.enlistmentHealthData.DirectoryHydrationLevels[2].HydratedFileCount.ShouldEqual(0);
            this.enlistmentHealthData.DirectoryHydrationLevels[2].TotalFileCount.ShouldEqual(4);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(pathData.GitFilePaths.Count);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual((decimal)pathData.PlaceholderFilePaths.Count / (decimal)pathData.GitFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);
        }

        [TestCase]
        public void AllEmptyLists()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
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
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddPlaceholderFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .AddModifiedPathFiles("A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(pathData.GitFilePaths.Count);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(pathData.ModifiedFilePaths.Count);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void SortByHydration()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("C/1.js", "A/1.js", "B/1.js", "B/2.js", "A/2.js", "C/2.js", "A/3.js", "C/3.js", "B/3.js")
                .AddModifiedPathFiles("C/1.js", "B/2.js", "A/3.js")
                .AddPlaceholderFiles("B/1.js", "A/2.js")
                .AddModifiedPathFiles("A/1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(pathData.ModifiedFilePaths.Count);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].Name.ShouldEqual("A");
            this.enlistmentHealthData.DirectoryHydrationLevels[1].Name.ShouldEqual("B");
            this.enlistmentHealthData.DirectoryHydrationLevels[2].Name.ShouldEqual("C");
        }

        [TestCase]
        public void VariedDirectoryFormatting()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddPlaceholderFolders("/A/", $"{this.sep}B{this.sep}", "A/", $"B{this.sep}", "/A", $"{this.sep}B", "A", "B")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFolderPaths.Count);

            // If the count of the sorted list is not 2, the different directory formats were considered distinct
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        [TestCase]
        public void VariedFilePathFormatting()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddPlaceholderFiles("A/1.js", $"A{this.sep}2.js", "/A/1.js", $"{this.sep}A{this.sep}1.js")
                .AddModifiedPathFiles($"B{this.sep}2.js", $"{this.sep}B{this.sep}1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(pathData.ModifiedFilePaths.Count);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(pathData.GitFilePaths.Count);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual((decimal)pathData.PlaceholderFilePaths.Count / (decimal)pathData.GitFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual((decimal)pathData.ModifiedFilePaths.Count / (decimal)pathData.GitFilePaths.Count);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].HydratedFileCount.ShouldEqual(4);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].TotalFileCount.ShouldEqual(3);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].HydratedFileCount.ShouldEqual(2);
            this.enlistmentHealthData.DirectoryHydrationLevels[1].TotalFileCount.ShouldEqual(3);
        }

        [TestCase]
        public void FilterByDirectory()
        {
            string[] gitFilesDirectoryA = new string[] { "A/1.js", "A/2.js", "A/3.js" };
            string[] gitFilesDirectoryB = new string[] { "B/1.js", "B/2.js", "B/3.js" };

            // Duplicate modified paths get cleaned up when unioned with non skip worktree paths from git
            // Duplicate placeholders remain as read from the placeholder database to most accurately represent information

            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles(gitFilesDirectoryA)
                .AddGitFiles(gitFilesDirectoryB)
                .AddPlaceholderFiles("A/1.js", $"A{this.sep}2.js", "/A/1.js", $"{this.sep}A{this.sep}1.js")
                .AddModifiedPathFiles("B/1.js", $"B{this.sep}2.js", "/B/1.js", $"{this.sep}B{this.sep}1.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, "A/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(gitFilesDirectoryA.Length);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(4.0m / 3.0m);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(0);

            this.enlistmentHealthData = this.GenerateStatistics(pathData, "/B/");

            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(gitFilesDirectoryB.Length);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(pathData.ModifiedFilePaths.Count);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(2.0m / 3.0m);
        }

        [TestCase]
        public void FilterByDirectoryWithoutPathSeparator()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("Directory1/Child1/File1.js", "Directory1/Child1/File2.exe", "Directory2/Child2/File1.bat", "Directory2/Child2/File2.css")
                .AddPlaceholderFiles("Directory1/File1.js", "Directory1/File2.exe", "Directory2/File1.bat", "Directory2/File2.css")
                .Build();

            // With no target should get both directories back
            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);

            // With a root target ('/') should also get both directories back
            this.enlistmentHealthData = this.GenerateStatistics(pathData, GVFSConstants.GitPathSeparatorString);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);

            // Filtering by a substring of a directory shouldn't get the directories back
            this.enlistmentHealthData = this.GenerateStatistics(pathData, "Directory");
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(0);
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(0);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(0);
        }

        [TestCase]
        public void EnsureFolderNotIncludedInOwnCount()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFolders("foo/")
                .AddGitFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            this.enlistmentHealthData.DirectoryHydrationLevels[0].HydratedFileCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].TotalFileCount.ShouldEqual(pathData.GitFilePaths.Count);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(pathData.GitFilePaths.Count + pathData.GitFolderPaths.Count);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(5m / 6m);
        }

        [TestCase]
        public void FolderNotDoubleCounted()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFolders("foo/")
                .AddGitFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFiles("foo/file1.jpg", "foo/file2.jpg", "foo/file3.jpg", "foo/file4.jpg", "foo/file5.jpg")
                .AddPlaceholderFolders("/foo")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].HydratedFileCount.ShouldEqual(pathData.PlaceholderFilePaths.Count);
            this.enlistmentHealthData.DirectoryHydrationLevels[0].TotalFileCount.ShouldEqual(pathData.GitFilePaths.Count);
            this.enlistmentHealthData.PlaceholderCount.ShouldEqual(pathData.PlaceholderFilePaths.Count + pathData.PlaceholderFolderPaths.Count);
            this.enlistmentHealthData.GitTrackedItemsCount.ShouldEqual(pathData.GitFilePaths.Count + pathData.GitFolderPaths.Count);
            this.enlistmentHealthData.PlaceholderPercentage.ShouldEqual(1);
        }

        [TestCase]
        public void UnionOfSkipWorktreeAndModifiedPathsNoDuplicates()
        {
            EnlistmentPathData pathData = new PathDataBuilder()
                .AddGitFiles("A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddModifiedPathFiles("A/1.js", "A/2.js", "/A/3.js", "B/1.js", "B/2.js", "B/3.js")
                .AddNonSkipWorktreeFiles("A/1.js", "/A/2.js", $"{this.sep}A/3.js", "B/1.js", $"B{this.sep}2.js", $"/B{this.sep}3.js")
                .Build();

            this.enlistmentHealthData = this.GenerateStatistics(pathData, string.Empty);

            // ModifiedPaths are unioned with NonSkipWorktreePaths to get a total count of fully git tracked files
            // The six ModifiedPaths overlap with the six NonSkipWorktreePaths, so only 6 should be counted
            this.enlistmentHealthData.ModifiedPathsCount.ShouldEqual(6);
            this.enlistmentHealthData.ModifiedPathsPercentage.ShouldEqual(1);
            this.enlistmentHealthData.DirectoryHydrationLevels.Count.ShouldEqual(2);
        }

        private EnlistmentHealthData GenerateStatistics(EnlistmentPathData pathData, string directory)
        {
            this.enlistmentHealthCalculator = new EnlistmentHealthCalculator(pathData);
            return this.enlistmentHealthCalculator.CalculateStatistics(directory);
        }

        public class PathDataBuilder
        {
            private readonly EnlistmentPathData pathData = new EnlistmentPathData();

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
                this.pathData.GitTrackingPaths.AddRange(nonSkipWorktreeFiles);
                return this;
            }

            public EnlistmentPathData Build()
            {
                this.pathData.NormalizeAllPaths();
                return this.pathData;
            }
        }
    }
}
