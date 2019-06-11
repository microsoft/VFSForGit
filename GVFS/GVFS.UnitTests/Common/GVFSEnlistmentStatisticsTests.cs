using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSEnlistmentStatisticsTests
    {
        // Chose this value because its the first digit that doesn't affect output (rounded to 2 decimal places)
        private const double Delta = 0.0001;

        private List<string> fauxGit = new List<string>();
        private List<string> fauxPlaceholderFiles = new List<string>();
        private List<string> fauxPlaceholderFolders = new List<string>();
        private List<string> fauxModifiedPathsFiles = new List<string>();
        private List<string> fauxModifiedPathsFolders = new List<string>();
        private GVFSEnlistmentStatistics enlistmentStatistics;

        [TestCase]
        public void OnlyOneHydratedDirectory()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[0].ShouldEqual("A");
            this.enlistmentStatistics.GetHydrationOfDirectory("A").ShouldEqualWithin(0.75, Delta);
            this.enlistmentStatistics.GetHydrationOfDirectory("B").ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetHydrationOfDirectory("C").ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(12);
            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(3);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(0);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(0.25, Delta);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(0, Delta);
        }

        [TestCase]
        public void AllEmptyLists()
        {
            this.SetUpStructures();

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(0);
            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(0);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(0);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(0, Delta);
        }

        [TestCase]
        public void OverHydrated()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "A/4.js", "B/1.js", "B/2.js", "B/3.js", "B/4.js", "C/1.js", "C/2.js", "C/3.js", "C/4.js" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(12);
            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(12);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(12);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(1, Delta);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(1, Delta);
        }

        [TestCase]
        public void SortByHydration()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "C/1.js", "A/1.js", "B/1.js", "B/2.js", "A/2.js", "C/2.js", "A/3.js", "C/3.js", "B/3.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "C/1.js", "B/2.js", "A/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "B/1.js", "A/2.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "A/1.js" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(2);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(4);
            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[0].ShouldEqual("A");
            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[1].ShouldEqual("B");
            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[2].ShouldEqual("C");
        }

        [TestCase]
        public void VariedDirectoryFormatting()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFolders.AddRange(new string[] { "/A/", @"\B\", "A/", @"B\", "/A", @"\B", "A", "B" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(8);

            // If the count of the sorted list is not 2, the different directory formats were considered distinct
            this.enlistmentStatistics.GetDirectoriesSortedByHydration().Count.ShouldEqual(2);
        }

        [TestCase]
        public void VariedFilePathFormatting()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", @"A\2.js", "/A/1.js", @"\A\1.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "B/1.js", @"B\2.js", "/B/1.js", @"\B\1.js" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(4);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(4);
            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(6);
            this.enlistmentStatistics.GetDirectoriesSortedByHydration().Count.ShouldEqual(2);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(2.0 / 3.0, Delta);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(2.0 / 3.0, Delta);
            this.enlistmentStatistics.GetHydrationOfDirectory("A").ShouldEqualWithin(4.0 / 3.0, Delta);
            this.enlistmentStatistics.GetHydrationOfDirectory("B").ShouldEqualWithin(4.0 / 3.0, Delta);
        }

        [TestCase]
        public void FilterByDirectory()
        {
            this.SetUpStructures();

            this.fauxGit.AddRange(new string[] { "A/1.js", "A/2.js", "A/3.js", "B/1.js", "B/2.js", "B/3.js" });
            this.fauxPlaceholderFiles.AddRange(new string[] { "A/1.js", @"A\2.js", "/A/1.js", @"\A\1.js" });
            this.fauxModifiedPathsFiles.AddRange(new string[] { "B/1.js", @"B\2.js", "/B/1.js", @"\B\1.js" });

            this.enlistmentStatistics = this.GenerateStatistics();

            this.enlistmentStatistics.CalculateStatistics("A");

            this.enlistmentStatistics.GetHydrationOfDirectory("B").ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(3);
            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(4);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(4.0 / 3.0, Delta);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(0);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[0].ShouldEqual("/");
            this.enlistmentStatistics.GetDirectoriesSortedByHydration().Count.ShouldEqual(1);

            this.enlistmentStatistics.CalculateStatistics("B");

            this.enlistmentStatistics.GetHydrationOfDirectory("A").ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetTrackedFilesCount().ShouldEqual(3);
            this.enlistmentStatistics.GetPlaceholderCount().ShouldEqual(0);
            this.enlistmentStatistics.GetPlaceholderPercentage().ShouldEqualWithin(0, Delta);
            this.enlistmentStatistics.GetModifiedPathsCount().ShouldEqual(4);
            this.enlistmentStatistics.GetModifiedPathsPercentage().ShouldEqualWithin(4.0 / 3.0, Delta);
            this.enlistmentStatistics.GetDirectoriesSortedByHydration()[0].ShouldEqual("/");
            this.enlistmentStatistics.GetDirectoriesSortedByHydration().Count.ShouldEqual(1);
        }

        private void SetUpStructures()
        {
            this.fauxGit.Clear();
            this.fauxPlaceholderFiles.Clear();
            this.fauxPlaceholderFolders.Clear();
            this.fauxModifiedPathsFiles.Clear();
            this.fauxModifiedPathsFolders.Clear();
        }

        private GVFSEnlistmentStatistics GenerateStatistics()
        {
            GVFSEnlistmentStatistics temp = new GVFSEnlistmentStatistics(
                this.fauxGit,
                this.fauxPlaceholderFolders,
                this.fauxPlaceholderFiles,
                this.fauxModifiedPathsFolders,
                this.fauxModifiedPathsFiles);
            temp.CalculateStatistics(string.Empty);
            return temp;
        }
    }
}
