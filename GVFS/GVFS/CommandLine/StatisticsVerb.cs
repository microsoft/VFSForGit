using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Virtualization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.CommandLine
{
    [Verb(StatisticsVerb.StatisticsVerbName, HelpText = "Get statistics for the health state of the repository")]
    public class StatisticsVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string StatisticsVerbName = "statistics";

        private const int DirectoryDisplayCount = 5;

        [Option(
            'c',
            "crawl",
            Required = false,
            HelpText = "Hydrate all files (for testing purposes)")]
        public bool ToCrawl { get; set; }

        protected override string VerbName
        {
            get { return StatisticsVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            // Debugging entry point to force the creation of placeholders
            if (this.ToCrawl)
            {
                this.Crawl();
            }

            this.GetPlaceholdersFromDatabase(enlistment, out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
            string[] modifiedPathsList = this.GetModifiedPathsFromPipe(enlistment);

            /* Failed code for creating a local instance of the modified paths database
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "Statistics"))
            {
                ModifiedPathsDatabase.TryLoadOrCreate(
                    tracer,
                    Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths),
                    new PhysicalFileSystem(),
                    out ModifiedPathsDatabase modifiedPathsDatabase,
                    out string err);
            }
            */

            int placeholderCount = filePlaceholders.Count + folderPlaceholders.Count;
            int modifiedPathsCount = modifiedPathsList.Length;

            Dictionary<string, int> trackedFilesLookup = this.ParseGitIndex(enlistment, out int trackedFilesCount);
            Dictionary<string, int> topLevelDirectoryHydrationLookup = new Dictionary<string, int>();

            // Update the dictionaries
            this.CategorizePathsString(modifiedPathsList, topLevelDirectoryHydrationLookup);
            this.CategorizePathsIPlaceholderData(filePlaceholders, topLevelDirectoryHydrationLookup);
            this.CategorizePathsIPlaceholderData(folderPlaceholders, topLevelDirectoryHydrationLookup);

            string trackedFilesCountFormatted = trackedFilesCount.ToString("N0");
            string placeholderCountFormatted = placeholderCount.ToString("N0");
            string modifiedPathsCountFormatted = modifiedPathsCount.ToString("N0");

            // Calculate spacing for the numbers of total files
            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            // Sort the dictionary to find the most hydrated directories by percentage
            List<KeyValuePair<string, int>> topLevelDirectoriesByHydration = topLevelDirectoryHydrationLookup.ToList();
            topLevelDirectoriesByHydration.Sort(
                delegate(KeyValuePair<string, int> pairOne, KeyValuePair<string, int> pairTwo)
                {
                    return ((double)pairOne.Value / trackedFilesLookup[pairOne.Key]).CompareTo((double)pairTwo.Value / trackedFilesLookup[pairTwo.Key]) * -1; // Multiply by -1 to reverse the sort
                });

            this.Output.WriteLine("\nRepository statistics");
            this.Output.WriteLine("Total paths tracked by git:     " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Total number of placeholders:   " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(((double)placeholderCount) / trackedFilesCount));
            this.Output.WriteLine("Total number of modified paths: " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(((double)modifiedPathsCount) / trackedFilesCount));

            this.Output.WriteLine("\nTotal hydration percentage:     " + this.FormatPercent((double)(placeholderCount + modifiedPathsCount) / trackedFilesCount).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxDirectoryNameLength = 0;
            for (int i = 0; i < 5 && i < topLevelDirectoriesByHydration.Count; i++)
            {
                maxDirectoryNameLength = Math.Max(maxDirectoryNameLength, topLevelDirectoriesByHydration[i].Key.Length);
            }

            for (int i = 0; i < DirectoryDisplayCount && i < topLevelDirectoriesByHydration.Count; i++)
            {
                string dir = topLevelDirectoriesByHydration[i].Key.PadRight(maxDirectoryNameLength);
                string percent = this.FormatPercent((double)topLevelDirectoriesByHydration[i].Value / trackedFilesLookup[topLevelDirectoriesByHydration[i].Key]);
                this.Output.WriteLine(" " + percent + " | " + dir);
            }

            bool healthyRepo = (placeholderCount + modifiedPathsCount) < (trackedFilesCount / 2);

            this.Output.WriteLine("\nRepository status: " + (healthyRepo ? "Healthy" : "Unhealthy"));

            Console.ReadLine();
        }

        /// <summary>
        /// Takes a fractional double and formats it as a percent taking exactly 4 characters with no decimals
        /// </summary>
        /// <param name="percent">Fractional double to format to a percent</param>
        /// <returns>A 4 character string formatting the percent correctly</returns>
        private string FormatPercent(double percent)
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
            return line.Substring(line.IndexOf("blob") + 46);
        }

        /// <summary>
        /// Take a string representing a file path on system and pull out the upper-most directory from it as a string, or '/' if it is in the root
        /// </summary>
        /// <param name="path">The path to a file to parse for the top level directory containing it</param>
        /// <returns>A string containing the top level directory from the provided path, or '/' if the path is for an item in the root</returns>
        private string ParseTopDirectory(string path)
        {
            int whackLocation = path.IndexOf('/');
            if (whackLocation == -1)
            {
                return "/";
            }

            return path.Substring(0, whackLocation);
        }

        /// <summary>
        /// Temporary function for intentionally creating additional placeholders for debugging purposes
        /// </summary>
        private void Crawl()
        {
            DirectoryInfo dirInfo = new DirectoryInfo("C:/repos/VFSForGit.git/");
            dirInfo.EnumerateDirectories()
                .AsParallel()
                .SelectMany(di => di.EnumerateFiles("*.*", SearchOption.AllDirectories))
                .Count();
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
                    NamedPipeMessages.Message modifiedPathsMessage = new NamedPipeMessages.Message(NamedPipeMessages.ModifiedPaths.ListRequest, "1");
                    pipeClient.SendRequest(modifiedPathsMessage);

                    NamedPipeMessages.Message modifiedPathsResponse = pipeClient.ReadResponse();
                    if (!modifiedPathsResponse.Header.Equals(NamedPipeMessages.ModifiedPaths.SuccessResult))
                    {
                        this.Output.WriteLine("Bad response from modified path pipe: " + modifiedPathsResponse.Header);
                        return modifiedPathsList;
                    }

                    modifiedPathsList = modifiedPathsResponse.Body.Split('\0');
                    modifiedPathsList = modifiedPathsList.Take(modifiedPathsList.Length - 1).ToArray();
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

        /// <summary>
        /// Use the git process class to list the tree at the head to count and classify the files being tracked by git
        /// Returns a dictionary mapping the top level directory to the number of files tracked inside of them
        /// </summary>
        /// <param name="enlistment">The current GVFS enlistment being operated on</param>
        /// <param name="totalTrackedFilesCount">Out parameter giving back the total number of files being tracked by git</param>
        /// <returns></returns>
        private Dictionary<string, int> ParseGitIndex(GVFSEnlistment enlistment, out int totalTrackedFilesCount)
        {
            Dictionary<string, int> trackedFilesLookup = new Dictionary<string, int>();
            int addedFiles = 0;

            GitProcess gitProcess = new GitProcess(enlistment);
            GitProcess.Result result = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line =>
                {
                    string topDir = this.ParseTopDirectory(this.TrimGitIndexLine(line));
                    trackedFilesLookup[topDir] = trackedFilesLookup.ContainsKey(topDir) ? trackedFilesLookup[topDir] + 1 : 1;
                    ++addedFiles;
                },
                recursive: true);

            totalTrackedFilesCount = addedFiles;
            return trackedFilesLookup;
        }

        /// <summary>
        /// Categorizes a list of paths given as strings by mapping them to the top level directory in their path
        /// Modifies the directoryTracking dictionary to have an accurate count of the files underneath a top level directory
        /// </summary>
        /// <remarks>
        /// The distinction between files and directories is important --
        /// If the path to a file doesn't contain a '/', then that means it falls within the root
        /// However if a directory's path doesn't contain a '/', it counts towards itself for hydration
        /// </remarks>
        /// <param name="paths">An enumerable containing paths as strings</param>
        /// <param name="directoryTracking">A dictionary used to track the number of files per top level directory</param>
        private void CategorizePathsString(IEnumerable<string> paths, Dictionary<string, int> directoryTracking)
        {
            foreach (string path in paths)
            {
                // Ensure all paths have the same slashes
                string formattedPath = path.Replace('\\', '/');

                // Making the assumption that all paths to files have the '.' character and that all paths to directories do not
                bool isFile = formattedPath.IndexOf('.') != -1;

                if (isFile)
                {
                    string topDir = this.ParseTopDirectory(formattedPath);
                    directoryTracking[topDir] = directoryTracking.ContainsKey(topDir) ? directoryTracking[topDir] + 1 : 1;
                }
                else
                {
                    string topDir = formattedPath;
                    if (topDir.IndexOf('/') != -1)
                    {
                        topDir = this.ParseTopDirectory(topDir);
                    }

                    directoryTracking[topDir] = directoryTracking.ContainsKey(topDir) ? directoryTracking[topDir] + 1 : 1;
                }
            }
        }

        /// <summary>
        /// Wrapper method to call <see cref="StatisticsVerb.CategorizePathsString(IEnumerable{string}, Dictionary{string, int})"/> on an enumerable of IPlaceholderData objects
        /// For example: <see cref="StatisticsVerb.GetPlaceholdersFromDatabase(GVFSEnlistment, out List{IPlaceholderData}, out List{IPlaceholderData})"/>
        /// </summary>
        /// <param name="placeholderData">An enumerable list of IPlaceholderData objects which each internally have a path</param>
        /// <param name="directoryTracking">A dictionary used to track the number of files per top level directory</param>
        private void CategorizePathsIPlaceholderData(IEnumerable<IPlaceholderData> placeholderData, Dictionary<string, int> directoryTracking)
        {
            string[] stringPaths = new string[placeholderData.Count()];
            for (int i = 0; i < stringPaths.Length; i++)
            {
                stringPaths[i] = placeholderData.ElementAt(i).Path;
            }

            this.CategorizePathsString(stringPaths, directoryTracking);
        }
    }
}