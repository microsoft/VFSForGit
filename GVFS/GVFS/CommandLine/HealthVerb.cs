using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(HealthVerb.HealthVerbName, HelpText = "EXPERIMENTAL FEATURE - Measure the health of the repository")]
    public class HealthVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string HealthVerbName = "health";
        private const decimal MaximumHealthyHydration = 0.5m;

        [Option(
            'n',
            Required = false,
            HelpText = "Only display the <n> most hydrated directories in the output")]
        public int DirectoryDisplayCount { get; set; } = 5;

        [Option(
            'd',
            "directory",
            Required = false,
            HelpText = "Get the health of a specific directory (default is the current working directory")]
        public string Directory { get; set; }

        [Option(
            's',
            "status",
            Required = false,
            HelpText = "Display only the hydration % of the repository, similar to 'git status' in a repository with sparse-checkout")]
        public bool StatusOnly { get; set; }

        protected override string VerbName => HealthVerbName;

        internal PhysicalFileSystem FileSystem { get; set; } = new PhysicalFileSystem();

        protected override void Execute(GVFSEnlistment enlistment)
        {
            if (this.StatusOnly)
            {
                this.OutputHydrationPercent(enlistment);
                return;
            }

            // Now default to the current working directory when running the verb without a specified path
            if (string.IsNullOrEmpty(this.Directory) || this.Directory.Equals("."))
            {
                if (Environment.CurrentDirectory.StartsWith(enlistment.WorkingDirectoryRoot, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    this.Directory = Environment.CurrentDirectory.Substring(enlistment.WorkingDirectoryRoot.Length);
                }
                else
                {
                    // If the path is not under the source root, set the directory to empty
                    this.Directory = string.Empty;
                }
            }

            this.Output.WriteLine("\nGathering repository data...");

            this.Directory = this.Directory.Replace(GVFSPlatform.GVFSPlatformConstants.PathSeparator, GVFSConstants.GitPathSeparator);

            EnlistmentPathData pathData = new EnlistmentPathData();

            pathData.LoadPlaceholdersFromDatabase(enlistment);
            pathData.LoadModifiedPaths(enlistment);
            pathData.LoadPathsFromGitIndex(enlistment);

            pathData.NormalizeAllPaths();

            EnlistmentHealthCalculator enlistmentHealthCalculator = new EnlistmentHealthCalculator(pathData);
            EnlistmentHealthData enlistmentHealthData = enlistmentHealthCalculator.CalculateStatistics(this.Directory);

            this.PrintOutput(enlistmentHealthData);
        }

        private void OutputHydrationPercent(GVFSEnlistment enlistment)
        {
            var summary = EnlistmentHydrationSummary.CreateSummary(enlistment, this.FileSystem);
            this.Output.WriteLine(summary.ToMessage());
        }

        private void PrintOutput(EnlistmentHealthData enlistmentHealthData)
        {
            string trackedFilesCountFormatted = enlistmentHealthData.GitTrackedItemsCount.ToString("N0");
            string placeholderCountFormatted = enlistmentHealthData.PlaceholderCount.ToString("N0");
            string modifiedPathsCountFormatted = enlistmentHealthData.ModifiedPathsCount.ToString("N0");

            // Calculate spacing for the numbers of total files
            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            // Sort the dictionary to find the most hydrated directories by health score
            List<EnlistmentHealthCalculator.SubDirectoryInfo> topLevelDirectoriesByHydration = enlistmentHealthData.DirectoryHydrationLevels.Take(this.DirectoryDisplayCount).ToList();

            this.Output.WriteLine("\nHealth of directory: " + enlistmentHealthData.TargetDirectory);
            this.Output.WriteLine("Total files in HEAD commit:           " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Files managed by VFS for Git (fast):  " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage));
            this.Output.WriteLine("Files managed by Git:                 " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.ModifiedPathsPercentage));

            this.Output.WriteLine("\nTotal hydration percentage:           " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage + enlistmentHealthData.ModifiedPathsPercentage).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxCountLength = 0;
            int maxTotalLength = 0;
            foreach (EnlistmentHealthCalculator.SubDirectoryInfo directoryInfo in topLevelDirectoriesByHydration)
            {
                maxCountLength = Math.Max(maxCountLength, directoryInfo.HydratedFileCount.ToString("N0").Length);
                maxTotalLength = Math.Max(maxTotalLength, directoryInfo.TotalFileCount.ToString("N0").Length);
            }

            foreach (EnlistmentHealthCalculator.SubDirectoryInfo directoryInfo in topLevelDirectoriesByHydration)
            {
                this.Output.WriteLine(" " + directoryInfo.HydratedFileCount.ToString("N0").PadLeft(maxCountLength) + " / " + directoryInfo.TotalFileCount.ToString("N0").PadRight(maxTotalLength) + " | " + directoryInfo.Name);
            }

            bool healthyRepo = (enlistmentHealthData.PlaceholderPercentage + enlistmentHealthData.ModifiedPathsPercentage) < MaximumHealthyHydration;

            this.Output.WriteLine("\nRepository status: " + (healthyRepo ? "OK" : "Highly Hydrated"));
        }

        /// <summary>
        /// Takes a fractional decimal and formats it as a percent taking exactly 4 characters with no decimals
        /// </summary>
        /// <param name="percent">Fractional decimal to format to a percent</param>
        /// <returns>A 4 character string formatting the percent correctly</returns>
        private string FormatPercent(decimal percent)
        {
            return percent.ToString("P0").PadLeft(4);
        }
    }
}
