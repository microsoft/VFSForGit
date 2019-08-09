using GVFS.Tests.Should;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture, Category(Categories.WindowsOnly)]
    public class HealthTests : TestsWithEnlistmentPerFixture
    {
        [TestCase, Order(0)]
        public void AfterCloningRepoIsPerfectlyHealthy()
        {
            // .gitignore is always a placeholder on creation
            // .gitconfig is always a modified path in functional tests since it is written at run time

            List<string> topHydratedDirectories = new List<string> { "GVFS", "GVFlt_BugRegressionTest", "GVFlt_DeleteFileTest", "GVFlt_DeleteFolderTest", "GVFlt_EnumTest" };
            List<int> directoryHydrationLevels = new List<int> { 0, 0, 0, 0, 0 };

            this.ValidateHealthOutputValues(
                directory: string.Empty,
                totalFiles: 873,
                totalFolders: 338,
                totalItems: 1211,
                totalItemPercent: 100,
                fastFiles: 1,
                fastFolders: 0,
                fastItems: 1,
                fastItemPercent: 0,
                slowFiles: 1,
                slowFolders: 0,
                slowItems: 1,
                slowItemPercent: 0,
                totalPercent: 0,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "OK");
        }

        [TestCase, Order(1)]
        public void PlaceholdersChangeHealthScores()
        {
            // Hydrate all files in the Scripts/ directory as placeholders
            // This creates 6 placeholders, 5 files along with the Scripts/ directory
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonAssemblyVersion.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonCliAssemblyVersion.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonVersionHeader.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunFunctionalTests.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunUnitTests.bat"));

            List<string> topHydratedDirectories = new List<string> { "Scripts", "GVFS", "GVFlt_BugRegressionTest", "GVFlt_DeleteFileTest", "GVFlt_DeleteFolderTest" };
            List<int> directoryHydrationLevels = new List<int> { 5, 0, 0, 0, 0 };

            this.ValidateHealthOutputValues(
                directory: string.Empty,
                totalFiles: 873,
                totalFolders: 338,
                totalItems: 1211,
                totalItemPercent: 100,
                fastFiles: 6,
                fastFolders: 1,
                fastItems: 7,
                fastItemPercent: 1,
                slowFiles: 1,
                slowFolders: 0,
                slowItems: 1,
                slowItemPercent: 0,
                totalPercent: 1,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "OK");
        }

        [TestCase, Order(2)]
        public void ModifiedPathsChangeHealthScores()
        {
            // Hydrate all files in GVFlt_FileOperationTest as modified paths
            // This creates 2 modified paths and one placeholder
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "GVFlt_FileOperationTest/DeleteExistingFile.txt"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "GVFlt_FileOperationTest/WriteAndVerify.txt"));

            List<string> topHydratedDirectories = new List<string> { "Scripts", "GVFlt_FileOperationTest", "GVFS", "GVFlt_BugRegressionTest", "GVFlt_DeleteFileTest" };
            List<int> directoryHydrationLevels = new List<int> { 5, 2, 0, 0, 0 };

            this.ValidateHealthOutputValues(
                directory: string.Empty,
                totalFiles: 873,
                totalFolders: 338,
                totalItems: 1211,
                totalItemPercent: 100,
                fastFiles: 6,
                fastFolders: 2,
                fastItems: 8,
                fastItemPercent: 1,
                slowFiles: 3,
                slowFolders: 0,
                slowItems: 3,
                slowItemPercent: 0,
                totalPercent: 1,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "OK");
        }

        [TestCase, Order(3)]
        public void TurnPlaceholdersIntoModifiedPaths()
        {
            // Hydrate the files in Scripts/ from placeholders to modified paths
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonAssemblyVersion.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonCliAssemblyVersion.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonVersionHeader.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunFunctionalTests.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunUnitTests.bat"));

            List<string> topHydratedDirectories = new List<string> { "Scripts", "GVFlt_FileOperationTest", "GVFS", "GVFlt_BugRegressionTest", "GVFlt_DeleteFileTest" };
            List<int> directoryHydrationLevels = new List<int> { 5, 2, 0, 0, 0 };

            this.ValidateHealthOutputValues(
                directory: string.Empty,
                totalFiles: 873,
                totalFolders: 338,
                totalItems: 1211,
                totalItemPercent: 100,
                fastFiles: 1,
                fastFolders: 2,
                fastItems: 3,
                fastItemPercent: 0,
                slowFiles: 8,
                slowFolders: 0,
                slowItems: 8,
                slowItemPercent: 1,
                totalPercent: 1,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "OK");
        }

        [TestCase, Order(4)]
        public void FilterIntoDirectory()
        {
            List<string> topHydratedDirectories = new List<string>();
            List<int> directoryHydrationLevels = new List<int>();

            this.ValidateHealthOutputValues(
                directory: "Scripts/",
                totalFiles: 5,
                totalFolders: 0,
                totalItems: 5,
                totalItemPercent: 100,
                fastFiles: 0,
                fastFolders: 0,
                fastItems: 0,
                fastItemPercent: 0,
                slowFiles: 5,
                slowFolders: 0,
                slowItems: 5,
                slowItemPercent: 100,
                totalPercent: 100,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "Highly Hydrated");
        }

        private void HydratePlaceholder(string filePath)
        {
            File.ReadAllText(filePath);
        }

        private void HydrateFullFile(string filePath)
        {
            File.OpenWrite(filePath).Close();
        }

        private void ValidateHealthOutputValues(
            string directory,
            int totalFiles,
            int totalFolders,
            int totalItems,
            int totalItemPercent,
            int fastFiles,
            int fastFolders,
            int fastItems,
            int fastItemPercent,
            int slowFiles,
            int slowFolders,
            int slowItems,
            int slowItemPercent,
            int totalPercent,
            List<string> topHydratedDirectories,
            List<int> directoryHydrationLevels,
            string enlistmentHealthStatus)
        {
            List<string> healthOutputLines = new List<string>(this.Enlistment.Health(directory).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

            int numberOfExpectedSubdirectories = topHydratedDirectories.Count;

            this.ValidateTargetDirectory(healthOutputLines[1], directory);
            this.ValidateTotalFileInfo(healthOutputLines[3], totalFiles, totalFolders, totalItems, totalItemPercent);
            this.ValidateFastFileInfo(healthOutputLines[4], fastFiles, fastFolders, fastItems, fastItemPercent);
            this.ValidateSlowFileInfo(healthOutputLines[5], slowFiles, slowFolders, slowItems, slowItemPercent);
            this.ValidateTotalHydration(healthOutputLines[6], totalPercent);
            this.ValidateSubDirectoryHealth(healthOutputLines.GetRange(8, numberOfExpectedSubdirectories), topHydratedDirectories, directoryHydrationLevels);
            this.ValidateEnlistmentStatus(healthOutputLines[8 + numberOfExpectedSubdirectories], enlistmentHealthStatus);
        }

        private void ValidateTargetDirectory(string outputLine, string targetDirectory)
        {
            // Regex to extract the target directory
            // "Health of directory: <directory>"
            Match lineMatch = Regex.Match(outputLine, @"^Health of directory:\s*(\S.*\S)\s*$");

            string outputtedTargetDirectory = lineMatch.Groups[1].Value;

            outputtedTargetDirectory.ShouldEqual(targetDirectory);
        }

        private void ValidateTotalFileInfo(string outputLine, int totalFiles, int totalFolders, int totalItems, int totalItemPercent)
        {
            // Regex to extract the total number of files and percentage they represent (should always be 100)
            // "Total files in HEAD commit:           <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Total files in HEAD commit:\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*(\d+)%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedTotalFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedTotalFolders).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[3].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedTotalItems).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[4].Value, out int outputtedTotalItemPercent).ShouldBeTrue();

            outputtedTotalFiles.ShouldEqual(totalFiles);
            outputtedTotalFolders.ShouldEqual(totalFolders);
            outputtedTotalItems.ShouldEqual(totalItems);
            outputtedTotalItemPercent.ShouldEqual(totalItemPercent);
        }

        private void ValidateFastFileInfo(string outputLine, int fastFiles, int fastFolders, int fastItems, int fastItemPercent)
        {
            // Regex to extract the total number of fast files and percentage they represent
            // "Files managed by VFS for Git (fast):    <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Files managed by VFS for Git \(faster\):\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*(\d+)%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedFastFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedFastFolders).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[3].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedFastItems).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[4].Value, out int outputtedFastItemPercent).ShouldBeTrue();

            outputtedFastFiles.ShouldEqual(fastFiles);
            outputtedFastFolders.ShouldEqual(fastFolders);
            outputtedFastItems.ShouldEqual(fastItems);
            outputtedFastItemPercent.ShouldEqual(fastItemPercent);
        }

        private void ValidateSlowFileInfo(string outputLine, int slowFiles, int slowFolders, int slowItems, int slowItemPercent)
        {
            // Regex to extract the total number of slow files and percentage they represent
            // "Files managed by git (slow):                <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Files managed by Git:\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*([\d,]+)\s*\|\s*(\d+)%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedSlowFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedSlowFolders).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[3].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedSlowItems).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[4].Value, out int outputtedSlowItemPercent).ShouldBeTrue();

            outputtedSlowFiles.ShouldEqual(slowFiles);
            outputtedSlowFolders.ShouldEqual(slowFolders);
            outputtedSlowItems.ShouldEqual(slowItems);
            outputtedSlowItemPercent.ShouldEqual(slowItemPercent);
        }

        private void ValidateTotalHydration(string outputLine, int totalHydration)
        {
            // Regex to extract the total hydration percentage of the enlistment
            // "Total hydration percentage:            <percentage>%
            Match lineMatch = Regex.Match(outputLine, @"^Total hydration percentage:\s*(\d+)%$");

            int.TryParse(lineMatch.Groups[1].Value, out int outputtedTotalHydration).ShouldBeTrue();

            outputtedTotalHydration.ShouldEqual(totalHydration);
        }

        private void ValidateSubDirectoryHealth(List<string> outputLines, List<string> subdirectories, List<int> healthScores)
        {
            for (int i = 0; i < outputLines.Count; i++)
            {
                // Regex to extract the most hydrated subdirectory names and their hydration percentage
                // "  <hydrated-file-count> / <total-file-count> | <directory-name>" listed several times for different directories
                Match lineMatch = Regex.Match(outputLines[i], @"^\s*([\d,]+)\s*/\s*([\d,]+)\s*\|\s*(\S.*\S)\s*$");

                int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedHealthScore).ShouldBeTrue();
                string outputtedSubdirectory = lineMatch.Groups[3].Value;

                outputtedHealthScore.ShouldEqual(healthScores[i]);
                outputtedSubdirectory.ShouldEqual(subdirectories[i]);
            }
        }

        private void ValidateEnlistmentStatus(string outputLine, string statusMessage)
        {
            // Regex to extract the status message for the enlistment
            // "Repository status: <status-message>"
            Match lineMatch = Regex.Match(outputLine, @"^Repository status:\s*(.*)$");

            string outputtedStatusMessage = lineMatch.Groups[1].Value;

            outputtedStatusMessage.ShouldEqual(statusMessage);
        }
    }
}
