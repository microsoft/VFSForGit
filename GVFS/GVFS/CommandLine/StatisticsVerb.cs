using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(StatisticsVerb.StatisticsVerbName, HelpText = "Get statistics for the health state of the repository")]
    public class StatisticsVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string StatisticsVerbName = "statistics";
        private const decimal MaximumHealthyHydration = 0.5m;

        [Option(
            'n',
            Required = false,
            HelpText = "The number of directories to display hydration levels for")]
        public int DirectoryDisplayCount { get; set; } = 5;

        [Option(
            'd',
            "directory",
            Required = false,
            HelpText = "Run the statistics tool on a specific directory")]
        public string Directory { get; set; }

        protected override string VerbName
        {
            get { return StatisticsVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            // The path to the root of git's tree
            string sourceRoot = enlistment.WorkingDirectoryRoot;

            if (this.Directory == null)
            {
                this.Directory = string.Empty;
            }
            else if (this.Directory.Equals("."))
            {
                this.Directory = Environment.CurrentDirectory.Substring(sourceRoot.Length);
            }

            // Get all of the data needed to calculate statistics then pass them into GVFSEnlistmentStatistics
            List<string> modifiedPathsFileList = new List<string>();
            List<string> modifiedPathsFolderList = new List<string>();
            List<string> placeholderFilePathList = new List<string>();
            List<string> placeholderFolderPathList = new List<string>();

            this.GetPlaceholdersFromDatabase(enlistment, out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
            foreach (IPlaceholderData placeholderData in filePlaceholders)
            {
                placeholderFilePathList.Add(placeholderData.Path);
            }

            foreach (IPlaceholderData placeholderData in folderPlaceholders)
            {
                placeholderFolderPathList.Add(placeholderData.Path);
            }

            foreach (string path in this.GetModifiedPathsFromPipe(enlistment))
            {
                if (path.Last() == GVFSConstants.GitPathSeparator)
                {
                    path.TrimEnd('/');
                    modifiedPathsFolderList.Add(path);
                }
                else
                {
                    modifiedPathsFileList.Add(path);
                }
            }

            List<string> gitPathsList = this.GetPathsFromGitIndex(enlistment);

            GVFSEnlistmentStatistics enlistmentStatistics = new GVFSEnlistmentStatistics(gitPathsList, placeholderFolderPathList, placeholderFilePathList, modifiedPathsFolderList, modifiedPathsFileList);
            enlistmentStatistics.CalculateStatistics(this.Directory);

            string trackedFilesCountFormatted = enlistmentStatistics.GitTrackedFilesCount.ToString("N0");
            string placeholderCountFormatted = enlistmentStatistics.PlaceholderCount.ToString("N0");
            string modifiedPathsCountFormatted = enlistmentStatistics.ModifiedPathsCount.ToString("N0");

            // Calculate spacing for the numbers of total files
            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            // Sort the dictionary to find the most hydrated directories by percentage
            List<string> topLevelDirectoriesByHydration = enlistmentStatistics.GetDirectoriesSortedByHydration();

            this.Output.WriteLine("\nRepository statistics");
            this.Output.WriteLine("Total files in HEAD commit:           " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Files managed by VFS for Git (fast):  " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentStatistics.GetPlaceholderPercentage()));
            this.Output.WriteLine("Files managed by git (slow):          " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentStatistics.GetModifiedPathsPercentage()));

            this.Output.WriteLine("\nTotal hydration percentage:           " + this.FormatPercent(enlistmentStatistics.GetPlaceholderPercentage() + enlistmentStatistics.GetModifiedPathsPercentage()).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxDirectoryNameLength = 0;
            for (int i = 0; i < 5 && i < topLevelDirectoriesByHydration.Count; i++)
            {
                maxDirectoryNameLength = Math.Max(maxDirectoryNameLength, topLevelDirectoriesByHydration[i].Length);
            }

            for (int i = 0; i < this.DirectoryDisplayCount && i < topLevelDirectoriesByHydration.Count; i++)
            {
                string dir = topLevelDirectoriesByHydration[i].PadRight(maxDirectoryNameLength);
                string percent = this.FormatPercent(enlistmentStatistics.GetHydrationOfDirectory(topLevelDirectoriesByHydration[i]));
                this.Output.WriteLine(" " + percent + " | " + dir);
            }

            bool healthyRepo = (enlistmentStatistics.GetPlaceholderPercentage() + enlistmentStatistics.GetModifiedPathsPercentage()) < MaximumHealthyHydration;

            this.Output.WriteLine("\nRepository status: " + (healthyRepo ? "Healthy" : "Unhealthy"));
            Console.ReadLine();
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
        private string[] GetModifiedPathsFromPipe(GVFSEnlistment enlistment)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                string[] modifiedPathsList = { };

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
                        return modifiedPathsList;
                    }

                    modifiedPathsList = modifiedPathsResponse.Body.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                }
                catch (BrokenPipeException e)
                {
                    this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
                }

                return modifiedPathsList;
            }
        }

        /// <summary>
        /// Get two lists of placeholders, one containing the files and the other the directories
        /// Goes to the SQLite database for the placeholder lists
        /// </summary>
        /// <param name="enlistment">The current GVFS enlistment being operated on</param>
        /// <param name="filePlaceholders">Out parameter where the list of file placeholders will end up</param>
        /// <param name="folderPlaceholders">Out parameter where the list of folder placeholders will end up</param>
        private void GetPlaceholdersFromDatabase(GVFSEnlistment enlistment, out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase());
            PlaceholderTable placeholderTable = new PlaceholderTable(database);

            placeholderTable.GetAllEntries(out filePlaceholders, out folderPlaceholders);
        }

        private List<string> GetPathsFromGitIndex(GVFSEnlistment enlistment)
        {
            List<string> gitPaths = new List<string>();
            GitProcess gitProcess = new GitProcess(enlistment);

            GitProcess.Result result = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line =>
                {
                    gitPaths.Add(this.TrimGitIndexLine(line));
                },
                recursive: true);
            return gitPaths;
        }
    }
}