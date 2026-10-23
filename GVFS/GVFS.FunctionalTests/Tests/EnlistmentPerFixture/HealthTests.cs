using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
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
                totalFiles: 1197,
                totalFilePercent: 100,
                fastFiles: 1,
                fastFilePercent: 0,
                slowFiles: 1,
                slowFilePercent: 0,
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
                totalFiles: 1197,
                totalFilePercent: 100,
                fastFiles: 7,
                fastFilePercent: 1,
                slowFiles: 1,
                slowFilePercent: 0,
                totalPercent:1,
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
                totalFiles: 1197,
                totalFilePercent: 100,
                fastFiles: 8,
                fastFilePercent: 1,
                slowFiles: 3,
                slowFilePercent: 0,
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
                totalFiles: 1197,
                totalFilePercent: 100,
                fastFiles: 3,
                fastFilePercent: 0,
                slowFiles: 8,
                slowFilePercent: 1,
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
                totalFilePercent: 100,
                fastFiles: 0,
                fastFilePercent: 0,
                slowFiles: 5,
                slowFilePercent: 100,
                totalPercent: 100,
                topHydratedDirectories: topHydratedDirectories,
                directoryHydrationLevels: directoryHydrationLevels,
                enlistmentHealthStatus: "Highly Hydrated");
        }

        [TestCase, Order(5)]
        public void StatusRecordsTheDisplayedResultAndFallbackDiagnostics()
        {
            this.RemountWithHydrationStatus(false);
            HashSet<string> previousLogs = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
            string output = this.Enlistment.Health(statusOnly: true);
            string log = Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log").Single(path => !previousLogs.Contains(path));
            string[] lines = File.ReadAllLines(log);
            string completion = lines.Single(line => line.Contains("health (Stop)"));
            using (JsonDocument document = JsonDocument.Parse(completion.Substring(completion.IndexOf('{'))))
            {
                JsonElement root = document.RootElement;
                Assert.AreEqual("Status", root.GetProperty("Mode").GetString());
                Guid.ParseExact(root.GetProperty("CommandId").GetString(), "N").ShouldNotEqual(Guid.Empty);
                JsonElement cache = root.GetProperty("Cache");
                cache.GetProperty("Outcome").GetString().ShouldEqual("NotAvailable");
                cache.GetProperty("DiagnosticsStatus").GetString().ShouldEqual("Parsed");
                Assert.IsTrue(cache.GetProperty("DiagnosticsAvailable").GetBoolean());
                cache.GetProperty("State").GetString().ShouldEqual("FeatureDisabled");
                string displayOutcome = root.GetProperty("DisplayOutcome").GetString();
                output.ShouldContain(displayOutcome == "Percentage" ? "% of files and " : "Error calculating hydration summary.");
                root.GetProperty("ResultSource").GetString().ShouldEqual("Fallback");
                string calculation = lines.Single(line => line.Contains("HydrationSummaryDuration "));
                using (JsonDocument calculationDocument = JsonDocument.Parse(calculation.Substring(calculation.IndexOf('{'))))
                {
                    JsonElement summary = calculationDocument.RootElement;
                    summary.GetProperty("CommandId").GetString().ShouldEqual(root.GetProperty("CommandId").GetString());
                    summary.GetProperty("CalculationId").GetString().ShouldEqual(root.GetProperty("CalculationId").GetString());
                    summary.GetProperty("FolderCountSource").GetString().ShouldEqual("IndexParser");
                    Assert.AreEqual(JsonValueKind.Number, summary.GetProperty("TotalFileCount").ValueKind);
                    Assert.AreEqual(JsonValueKind.Number, summary.GetProperty("PlaceholderFolderCount").ValueKind);
                }
            }
        }

        [TestCase, Order(6)]
        public void DetailedHealthRecordsItsCommandMode()
        {
            HashSet<string> previous = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
            this.Enlistment.Health();
            using (JsonDocument completion = this.ReadHealthCompletion(previous))
            {
                completion.RootElement.GetProperty("Mode").GetString().ShouldEqual("Detailed");
                completion.RootElement.GetProperty("Outcome").GetString().ShouldEqual("Success");
                Assert.IsFalse(completion.RootElement.TryGetProperty("Cache", out _));
            }
        }

        [TestCase, Order(7)]
        public void CachedStatusRecordsThePublishedCalculationIdentity()
        {
            this.RemountWithHydrationStatus(true);
            Stopwatch deadline = Stopwatch.StartNew();
            string lastCompletion = string.Empty;
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                HashSet<string> previous = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
                string output = this.Enlistment.Health(statusOnly: true);
                using (JsonDocument completion = this.ReadHealthCompletion(previous))
                {
                    JsonElement root = completion.RootElement;
                    lastCompletion = root.GetRawText();
                    if (root.GetProperty("ResultSource").GetString() == "Cache")
                    {
                        JsonElement cache = root.GetProperty("Cache");
                        cache.GetProperty("Outcome").GetString().ShouldEqual("Success");
                        cache.GetProperty("DiagnosticsStatus").GetString().ShouldEqual("Parsed");
                        cache.GetProperty("State").GetString().ShouldEqual("Available");
                        Assert.IsTrue(cache.GetProperty("CountsComplete").GetBoolean());
                        Assert.That(cache.GetProperty("AgeMs").GetInt64(), Is.GreaterThanOrEqualTo(0));
                        Guid.ParseExact(cache.GetProperty("CalculationId").GetString(), "N").ShouldNotEqual(Guid.Empty);
                        root.GetProperty("CalculationId").GetString().ShouldEqual(cache.GetProperty("CalculationId").GetString());
                        root.GetProperty("Outcome").GetString().ShouldEqual("Success");
                        root.GetProperty("CalculationOutcome").GetString().ShouldEqual("Success");
                        output.ShouldContain("% of files and ");
                        return;
                    }
                }

                Thread.Sleep(100);
            }

            Assert.Fail("The mount did not publish a valid hydration summary. Last completion: " + lastCompletion);
        }

        private void RemountWithHydrationStatus(bool enabled)
        {
            this.Enlistment.UnmountGVFS();
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.show-hydration-status " + (enabled ? "true" : "false")).ExitCode.ShouldEqual(0);
            this.Enlistment.MountGVFS();
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "status --porcelain").ExitCode.ShouldEqual(0);
            this.Enlistment.WaitForBackgroundOperations();
        }

        private JsonDocument ReadHealthCompletion(HashSet<string> previous)
        {
            string log = Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log").Single(path => !previous.Contains(path));
            string line = File.ReadAllLines(log).Single(value => value.Contains("health (Stop)"));
            return JsonDocument.Parse(line.Substring(line.IndexOf('{')));
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
            int totalFilePercent,
            int fastFiles,
            int fastFilePercent,
            int slowFiles,
            int slowFilePercent,
            int totalPercent,
            List<string> topHydratedDirectories,
            List<int> directoryHydrationLevels,
            string enlistmentHealthStatus)
        {
            this.Enlistment.WaitForBackgroundOperations();
            string rawOutput = this.Enlistment.Health(directory);
            List<string> healthOutputLines = new List<string>(rawOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

            int numberOfExpectedSubdirectories = topHydratedDirectories.Count;
            int expectedMinimumLines = 8 + numberOfExpectedSubdirectories;

            if (healthOutputLines.Count < expectedMinimumLines)
            {
                Assert.Fail(
                    $"Expected at least {expectedMinimumLines} lines in 'gvfs health' output, but got {healthOutputLines.Count}.\n" +
                    $"Raw output:\n{rawOutput}");
            }

            this.ValidateTargetDirectory(healthOutputLines[1], directory);
            this.ValidateTotalFileInfo(healthOutputLines[2], totalFiles, totalFilePercent);
            this.ValidateFastFileInfo(healthOutputLines[3], fastFiles, fastFilePercent);
            this.ValidateSlowFileInfo(healthOutputLines[4], slowFiles, slowFilePercent);
            this.ValidateTotalHydration(healthOutputLines[5], totalPercent);
            this.ValidateSubDirectoryHealth(healthOutputLines.GetRange(7, numberOfExpectedSubdirectories), topHydratedDirectories, directoryHydrationLevels);
            this.ValidateEnlistmentStatus(healthOutputLines[7 + numberOfExpectedSubdirectories], enlistmentHealthStatus);
        }

        private void ValidateTargetDirectory(string outputLine, string targetDirectory)
        {
            // Regex to extract the target directory
            // "Health of directory: <directory>"
            Match lineMatch = Regex.Match(outputLine, @"^Health of directory:\s*(.*)$");

            string outputtedTargetDirectory = lineMatch.Groups[1].Value;

            outputtedTargetDirectory.ShouldEqual(targetDirectory);
        }

        private void ValidateTotalFileInfo(string outputLine, int totalFiles, int totalFilePercent)
        {
            // Regex to extract the total number of files and percentage they represent (should always be 100)
            // "Total files in HEAD commit:           <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Total files in HEAD commit:\s*([\d,]+)\s*\|\s*(\d+)\s*%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedTotalFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, out int outputtedTotalFilePercent).ShouldBeTrue();

            outputtedTotalFiles.ShouldEqual(totalFiles);
            outputtedTotalFilePercent.ShouldEqual(totalFilePercent);
        }

        private void ValidateFastFileInfo(string outputLine, int fastFiles, int fastFilesPercent)
        {
            // Regex to extract the total number of fast files and percentage they represent
            // "Files managed by VFS for Git (fast):    <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Files managed by VFS for Git \(fast\):\s*([\d,]+)\s*\|\s*(\d+)\s*%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedFastFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, out int outputtedFastFilesPercent).ShouldBeTrue();

            outputtedFastFiles.ShouldEqual(fastFiles);
            outputtedFastFilesPercent.ShouldEqual(fastFilesPercent);
        }

        private void ValidateSlowFileInfo(string outputLine, int slowFiles, int slowFilesPercent)
        {
            // Regex to extract the total number of slow files and percentage they represent
            // "Files managed by git (slow):                <count> | <percentage>%"
            Match lineMatch = Regex.Match(outputLine, @"^Files managed by Git:\s*([\d,]+)\s*\|\s*(\d+)\s*%$");

            int.TryParse(lineMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.CurrentCulture.NumberFormat, out int outputtedSlowFiles).ShouldBeTrue();
            int.TryParse(lineMatch.Groups[2].Value, out int outputtedSlowFilesPercent).ShouldBeTrue();

            outputtedSlowFiles.ShouldEqual(slowFiles);
            outputtedSlowFilesPercent.ShouldEqual(slowFilesPercent);
        }

        private void ValidateTotalHydration(string outputLine, int totalHydration)
        {
            // Regex to extract the total hydration percentage of the enlistment
            // "Total hydration percentage:            <percentage>%
            Match lineMatch = Regex.Match(outputLine, @"^Total hydration percentage:\s*(\d+)\s*%$");

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
            // Regex to extract the status message for the enlistment. The verb prints
            // "Repository status: <status-message>" when the calculation covers the whole enlistment,
            // and "Directory status (<path>): <status-message>" when scoped to a subdirectory.
            Match lineMatch = Regex.Match(outputLine, @"^(?:Repository status|Directory status \([^)]*\)):\s*(.*)$");

            string outputtedStatusMessage = lineMatch.Groups[1].Value;

            outputtedStatusMessage.ShouldEqual(statusMessage);
        }
    }
}
