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
using System.Text;
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

        [TestCase(0), Order(5)]
        [TestCase(5)]
        public void StatusRecordsTheDisplayedResultAndFallbackDiagnostics(int backoffSeconds)
        {
            this.RemountWithHydrationStatus(false, backoffSeconds);
            JsonElement root = this.WaitForHydrationCacheState("FeatureDisabled", out string output, out string[] lines);
            Assert.AreEqual("Status", root.GetProperty("Mode").GetString());
            Guid.ParseExact(root.GetProperty("CommandId").GetString(), "N").ShouldNotEqual(Guid.Empty);
            JsonElement cache = root.GetProperty("Cache");
            cache.GetProperty("Outcome").GetString().ShouldEqual("NotAvailable");
            cache.GetProperty("DiagnosticsStatus").GetString().ShouldEqual("Parsed");
            Assert.IsTrue(cache.GetProperty("DiagnosticsAvailable").GetBoolean());
            cache.GetProperty("State").GetString().ShouldEqual("FeatureDisabled");
            string displayOutcome = root.GetProperty("DisplayOutcome").GetString();
            output.ShouldContain(displayOutcome == "Percentage" ? "Estimated hydration: " : "Error calculating hydration summary.");
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

        [TestCase, Order(6)]
        public void DetailedHealthRecordsItsCommandMode()
        {
            HashSet<string> previous = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
            this.Enlistment.Health();
            using (JsonDocument completion = this.ReadHealthCompletion(previous, out _))
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
            JsonElement root = this.WaitForHydrationCacheState("Available", out string output, out _);
            root.GetProperty("ResultSource").GetString().ShouldEqual("Cache");
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
            output.ShouldContain("Estimated hydration: ");
        }

        [TestCase(true), Order(8)]
        [TestCase(false)]
        public void StatusToleratesRetainedFoldersAndNewFilesAfterBranchSwitch(bool cached)
        {
            string originalCommit = this.RunGit("rev-parse HEAD").Trim();
            string retainedFolder = Path.Combine(this.Enlistment.RepoRoot, "hydration-old-retained");
            string removedFolder = Path.Combine(this.Enlistment.RepoRoot, "hydration-old-clean");
            string newFolder = Path.Combine(this.Enlistment.RepoRoot, "hydration-untracked");
            string untrackedFile = Path.Combine(retainedFolder, "untracked.txt");
            string[] newFiles = Enumerable.Range(0, 3).Select(i => Path.Combine(this.Enlistment.RepoRoot, "hydration-new-" + i + ".txt")).ToArray();
            bool oldBranchCreated = false;
            bool currentBranchCreated = false;
            try
            {
                string blob = this.RunGitWithInput("hash-object -w --stdin", "tracked\n");
                string subtree = this.RunGitWithInput("mktree", "100644 blob " + blob + "\ttracked.txt\n");
                string oldTree = this.RunGitWithInput("mktree",
                    "040000 tree " + subtree + "\thydration-old-clean\n040000 tree " + subtree + "\thydration-old-retained\n");
                string newTree = this.RunGitWithInput("mktree", "040000 tree " + subtree + "\thydration-current\n");
                string oldCommit = this.RunGitWithInput("commit-tree " + oldTree + " -p " + originalCommit, "Old hydration population\n");
                string newCommit = this.RunGitWithInput("commit-tree " + newTree + " -p " + oldCommit, "Current hydration population\n");
                this.RunGit("branch hydration-count-old " + oldCommit);
                oldBranchCreated = true;
                this.RunGit("branch hydration-count-current " + newCommit);
                currentBranchCreated = true;
                this.RunGit("checkout -f hydration-count-old");
                File.ReadAllText(Path.Combine(retainedFolder, "tracked.txt")).ShouldEqual("tracked\n");
                File.ReadAllText(Path.Combine(removedFolder, "tracked.txt")).ShouldEqual("tracked\n");
                File.WriteAllText(untrackedFile, "keep this folder");
                this.Enlistment.WaitForBackgroundOperations();

                this.RunGit("checkout hydration-count-current");
                File.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "hydration-current", "tracked.txt")).ShouldEqual("tracked\n");
                Directory.CreateDirectory(newFolder);
                File.WriteAllText(Path.Combine(newFolder, "new.txt"), "new untracked folder");
                foreach (string file in newFiles)
                {
                    File.WriteAllText(file, "new untracked file");
                }

                this.Enlistment.WaitForBackgroundOperations();
                File.ReadAllText(untrackedFile).ShouldEqual("keep this folder");
                Assert.IsFalse(Directory.Exists(removedFolder), "A clean obsolete folder must be removed, unlike the retained folder.");
                string database = Path.Combine(this.Enlistment.DotGVFSRoot, "databases", "VFSForGit.sqlite");
                string placeholders = GVFSHelpers.GetAllSQLitePlaceholdersAsString(database);
                placeholders.ShouldContain("hydration-old-retained" + GVFSHelpers.PlaceholderFieldDelimiter);
                Assert.That(placeholders, Does.Not.Contain("hydration-old-clean" + GVFSHelpers.PlaceholderFieldDelimiter));

                this.RemountWithHydrationStatus(cached, backoffSeconds: 0);
                JsonElement completion = this.WaitForHydrationCacheState(cached ? "Available" : "FeatureDisabled", out string output, out string[] lines);
                completion.GetProperty("Outcome").GetString().ShouldEqual("Success");
                completion.GetProperty("CalculationOutcome").GetString().ShouldEqual("Success");
                completion.GetProperty("ResultSource").GetString().ShouldEqual(cached ? "Cache" : "Fallback");
                output.ShouldContain("Estimated hydration: files 100% (counts exceed current total); folders 100% (counts exceed current total)");
                JsonElement result = completion.GetProperty(cached ? "Cache" : "Summary");
                JsonElement counts = cached ? result.GetProperty("Counts") : result;
                counts.GetProperty("TotalFileCount").GetInt32().ShouldEqual(1);
                counts.GetProperty("TotalFolderCount").GetInt32().ShouldEqual(1);
                Assert.That(result.GetProperty("FileCountExcess").GetInt64(), Is.GreaterThan(0));
                Assert.That(result.GetProperty("FolderCountExcess").GetInt64(), Is.GreaterThan(0));
                result.GetProperty("FolderCountSource").GetString().ShouldEqual(cached ? "Projection" : "IndexParser");
                Assert.IsTrue(result.GetProperty("CountsComplete").GetBoolean());
                if (cached)
                {
                    int calculations = this.CountMountHydrationCalculations();
                    string calculationId = completion.GetProperty("CalculationId").GetString();
                    for (int i = 0; i < 3; i++)
                    {
                        HashSet<string> previous = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
                        this.Enlistment.Health(statusOnly: true).ShouldEqual(output);
                        using (JsonDocument repeated = this.ReadHealthCompletion(previous, out string[] repeatedLines))
                        {
                            repeated.RootElement.GetProperty("ResultSource").GetString().ShouldEqual("Cache");
                            repeated.RootElement.GetProperty("CalculationId").GetString().ShouldEqual(calculationId);
                            Assert.IsFalse(repeatedLines.Any(line => line.Contains("HydrationSummaryDuration ")));
                        }
                    }

                    this.CountMountHydrationCalculations().ShouldEqual(calculations);
                    Stopwatch hookDeadline = Stopwatch.StartNew();
                    string hookOutput;
                    do
                    {
                        ProcessResult status = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "status");
                        status.ExitCode.ShouldEqual(0);
                        hookOutput = status.Output + status.Errors;
                    }
                    while (!hookOutput.Contains("Estimated hydration: ") && hookDeadline.Elapsed < TimeSpan.FromSeconds(30));

                    hookOutput.ShouldContain("Estimated hydration: ");
                }
                else
                {
                    string calculation = lines.Single(line => line.Contains("HydrationSummaryDuration "));
                    using (JsonDocument record = JsonDocument.Parse(calculation.Substring(calculation.IndexOf('{'))))
                    {
                        record.RootElement.GetProperty("Outcome").GetString().ShouldEqual("Success");
                        record.RootElement.GetProperty("ValidationFailures").GetString().ShouldContain("HydratedFilesExceedTotal");
                        record.RootElement.GetProperty("ValidationFailures").GetString().ShouldContain("HydratedFoldersExceedTotal");
                    }
                }
            }
            finally
            {
                foreach (string file in newFiles)
                {
                    File.Delete(file);
                }

                if (File.Exists(untrackedFile))
                {
                    File.Delete(untrackedFile);
                }

                if (Directory.Exists(newFolder))
                {
                    Directory.Delete(newFolder, recursive: true);
                }

                this.RunGit("checkout -f " + originalCommit);
                if (oldBranchCreated)
                {
                    this.RunGit("branch -D hydration-count-old");
                }

                if (currentBranchCreated)
                {
                    this.RunGit("branch -D hydration-count-current");
                }

                this.Enlistment.WaitForBackgroundOperations();
            }
        }

        private int CountMountHydrationCalculations()
        {
            return Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_mount_*.log")
                .Sum(path => GVFSHelpers.ReadAllTextFromWriteLockedFile(path).Split('\n').Count(line => line.Contains("HydrationSummaryDuration ")));
        }

        private string RunGitWithInput(string command, string input)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
            {
                ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, inputStream: stream);
                result.ExitCode.ShouldEqual(0, result.Errors);
                return result.Output.Trim();
            }
        }

        private string RunGit(string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, result.Errors);
            return result.Output;
        }

        private JsonElement WaitForHydrationCacheState(string expectedState, out string output, out string[] lines)
        {
            Stopwatch deadline = Stopwatch.StartNew();
            string lastCompletion = string.Empty;
            output = string.Empty;
            lines = Array.Empty<string>();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                HashSet<string> previous = new HashSet<string>(Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log"));
                output = this.Enlistment.Health(statusOnly: true);
                using (JsonDocument completion = this.ReadHealthCompletion(previous, out lines))
                {
                    JsonElement root = completion.RootElement;
                    lastCompletion = root.GetRawText();
                    if (root.GetProperty("Cache").GetProperty("State").GetString() == expectedState)
                    {
                        return root.Clone();
                    }
                }

                Thread.Sleep(100);
            }

            throw new AssertionException("The hydration cache did not reach " + expectedState + ". Last completion: " + lastCompletion);
        }

        private void RemountWithHydrationStatus(bool enabled, int backoffSeconds = 2)
        {
            this.Enlistment.UnmountGVFS();
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.show-hydration-status " + (enabled ? "true" : "false")).ExitCode.ShouldEqual(0);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.status-cache-backoff-seconds " + backoffSeconds).ExitCode.ShouldEqual(0);
            this.Enlistment.MountGVFS();
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "status --porcelain").ExitCode.ShouldEqual(0);
            this.Enlistment.WaitForBackgroundOperations();
        }

        private JsonDocument ReadHealthCompletion(HashSet<string> previous, out string[] lines)
        {
            string log = Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_health_*.log").Single(path => !previous.Contains(path));
            lines = File.ReadAllLines(log);
            string line = lines.Single(value => value.Contains("health (Stop)"));
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
