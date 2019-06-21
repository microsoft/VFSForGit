using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(HealthVerb.HealthVerbName, HelpText = "Measure the health of the repository")]
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
            HelpText = "Run the statistics tool on a specific directory")]
        public string Directory { get; set; }

        protected override string VerbName => HealthVerbName;

        protected override void Execute(GVFSEnlistment enlistment)
        {
            // Now default to the current working directory when running the verb without a specified path
            if (string.IsNullOrEmpty(this.Directory) || this.Directory.Equals("."))
            {
                if (Environment.CurrentDirectory.StartsWith(enlistment.WorkingDirectoryRoot))
                {
                    this.Directory = Environment.CurrentDirectory.Substring(enlistment.WorkingDirectoryRoot.Length);
                }
                else
                {
                    // If the path is not under the source root, set the directory to empty
                    this.Directory = string.Empty;
                }
            }

            this.Directory = this.Directory.Replace('\\', GVFSConstants.GitPathSeparator);

            GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData = new GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData();

            this.GetPathsFromGitIndex(enlistment, pathData);
            this.GetPlaceholdersFromDatabase(enlistment, pathData);
            this.GetModifiedPathsFromPipe(enlistment, pathData);

            pathData.NormalizeAllPaths();

            GVFSEnlistmentHealthCalculator enlistmentHealthCalculator = new GVFSEnlistmentHealthCalculator(pathData);
            GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData enlistmentHealthData = enlistmentHealthCalculator.CalculateStatistics(this.Directory);

            this.PrintOutput(enlistmentHealthData);
        }

        private void PrintOutput(GVFSEnlistmentHealthCalculator.GVFSEnlistmentHealthData enlistmentHealthData)
        {
            string trackedFilesCountFormatted = enlistmentHealthData.GitTrackedItemsCount.ToString("N0");
            string placeholderCountFormatted = enlistmentHealthData.PlaceholderCount.ToString("N0");
            string modifiedPathsCountFormatted = enlistmentHealthData.ModifiedPathsCount.ToString("N0");

            // Calculate spacing for the numbers of total files
            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            // Sort the dictionary to find the most hydrated directories by percentage
            List<KeyValuePair<string, decimal>> topLevelDirectoriesByHydration = enlistmentHealthData.DirectoryHydrationLevels.Take(this.DirectoryDisplayCount).ToList();

            this.Output.WriteLine("\nHealth of directory: " + enlistmentHealthData.TargetDirectory);
            this.Output.WriteLine("Total files in HEAD commit:           " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Files managed by VFS for Git (fast):  " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage));
            this.Output.WriteLine("Files managed by git (slow):          " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.ModifiedPathsPercentage));

            this.Output.WriteLine("\nTotal hydration percentage:           " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage + enlistmentHealthData.ModifiedPathsPercentage).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxDirectoryNameLength = 0;
            foreach (KeyValuePair<string, decimal> pair in topLevelDirectoriesByHydration)
            {
                maxDirectoryNameLength = Math.Max(maxDirectoryNameLength, pair.Key.Length);
            }

            foreach (KeyValuePair<string, decimal> pair in topLevelDirectoriesByHydration)
            {
                string dir = pair.Key.PadRight(maxDirectoryNameLength);
                string percent = this.FormatPercent(pair.Value);
                this.Output.WriteLine(" " + percent + " | " + dir);
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

        /// <summary>
        /// Parse a line of the git index coming from the ls-tree endpoint in the git process to get the path to that file
        /// </summary>
        /// <param name="line">The line from the output of the git index</param>
        /// <returns>The path extracted from the provided line of the git index</returns>
        private string TrimGitIndexLine(string line)
        {
            return line.Substring(line.IndexOf('\t') + 1);
        }

        /// <summary>
        /// Talk to the mount process across the named pipe to get a list of the modified paths
        /// </summary>
        /// <param name="enlistment">The enlistment being operated on</param>
        /// <returns>An array containing all of the modified paths in string format</returns>
        private void GetModifiedPathsFromPipe(GVFSEnlistment enlistment, GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                string[] modifiedPathsList = Array.Empty<string>();

                if (!pipeClient.Connect())
                {
                    this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                }

                try
                {
                    NamedPipeMessages.Message modifiedPathsMessage = new NamedPipeMessages.Message(NamedPipeMessages.ModifiedPaths.ListRequest, NamedPipeMessages.ModifiedPaths.CurrentVersion);
                    pipeClient.SendRequest(modifiedPathsMessage);

                    NamedPipeMessages.Message modifiedPathsResponse = pipeClient.ReadResponse();
                    if (!modifiedPathsResponse.Header.Equals(NamedPipeMessages.ModifiedPaths.SuccessResult))
                    {
                        this.Output.WriteLine("Bad response from modified path pipe: " + modifiedPathsResponse.Header);
                        return;
                    }

                    modifiedPathsList = modifiedPathsResponse.Body.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                }
                catch (BrokenPipeException e)
                {
                    this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
                }

                foreach (string path in modifiedPathsList)
                {
                    if (path.Last() == GVFSConstants.GitPathSeparator)
                    {
                        path.TrimEnd(GVFSConstants.GitPathSeparator);
                        pathData.ModifiedFolderPaths.Add(path);
                    }
                    else
                    {
                        pathData.ModifiedFilePaths.Add(path);
                    }
                }
            }
        }

        /// <summary>
        /// Get two lists of placeholders, one containing the files and the other the directories
        /// Goes to the SQLite database for the placeholder lists
        /// </summary>
        /// <param name="enlistment">The current GVFS enlistment being operated on</param>
        /// <param name="filePlaceholders">Out parameter where the list of file placeholders will end up</param>
        /// <param name="folderPlaceholders">Out parameter where the list of folder placeholders will end up</param>
        private void GetPlaceholdersFromDatabase(GVFSEnlistment enlistment, GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData)
        {
            List<IPlaceholderData> filePlaceholders = new List<IPlaceholderData>();
            List<IPlaceholderData> folderPlaceholders = new List<IPlaceholderData>();

            using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase()))
            {
                PlaceholderTable placeholderTable = new PlaceholderTable(database);
                placeholderTable.GetAllEntries(out filePlaceholders, out folderPlaceholders);
            }

            pathData.PlaceholderFilePaths.AddRange(filePlaceholders.Select(placeholderData => placeholderData.Path));
            pathData.PlaceholderFolderPaths.AddRange(folderPlaceholders.Select(placeholderData => placeholderData.Path));
        }

        private void GetPathsFromGitIndex(GVFSEnlistment enlistment, GVFSEnlistmentHealthCalculator.GVFSEnlistmentPathData pathData)
        {
            List<string> skipWorktreeFiles = new List<string>();
            GitProcess gitProcess = new GitProcess(enlistment);

            GitProcess.Result fileResult = gitProcess.LsFiles(
                line =>
                {
                    if (line.First() == 'S')
                    {
                        skipWorktreeFiles.Add(this.TrimGitIndexLine(line));
                    }

                    pathData.GitFilePaths.Add(this.TrimGitIndexLine(line));
                },
                showSkipTreeBit: true);
            GitProcess.Result folderResult = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line =>
                {
                    pathData.GitFolderPaths.Add(this.TrimGitIndexLine(line));
                },
                recursive: true,
                showDirectories: true);

            pathData.AddSkipWorkTreeFilePaths(skipWorktreeFiles);
        }
    }
}
