using GVFS.Common.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Class responsible for the business logic involved in calculating the health statistics
    /// of a gvfs enlistment. Constructed with the lists of paths for the enlistment, and then
    /// internally stores the calculated information. Compute or recompute via CalculateStatistics
    /// with an optional parameter to only look for paths which are under the specified directory
    /// </summary>
    public class GVFSEnlistmentHealthCalculator
    {
        // In the context of this project, hydrated files are placeholders or modified paths
        // The total number of hydrated files is this.PlaceholderCount + this.ModifiedPathsCount
        private readonly List<string> gitPaths;
        private readonly List<string> placeholderFolderPaths;
        private readonly List<string> placeholderFilePaths;
        private readonly List<string> modifiedFolderPaths;
        private readonly List<string> modifiedFilePaths;

        public GVFSEnlistmentHealthCalculator(
            List<string> gitPaths,
            List<string> placeholderFolderPaths,
            List<string> placeholderFilePaths,
            List<string> modifiedFolderPaths,
            List<string> modifiedFilePaths)
        {
            this.gitPaths = gitPaths;
            this.placeholderFolderPaths = placeholderFolderPaths;
            this.placeholderFilePaths = placeholderFilePaths;
            this.modifiedFolderPaths = modifiedFolderPaths;
            this.modifiedFilePaths = modifiedFilePaths;
        }

        public GVFSEnlistmentHealthData CalculateStatistics(string parentDirectory, int directoryOutputCount)
        {
            int gitTrackedFilesCount = 0;
            int placeholderCount = 0;
            int modifiedPathsCount = 0;
            Dictionary<string, int> gitTrackedFilesDirectoryTally = new Dictionary<string, int>();
            Dictionary<string, int> hydratedFilesDirectoryTally = new Dictionary<string, int>();

            if (!parentDirectory.EndsWith(GVFSConstants.GitPathSeparatorString) && parentDirectory.Length > 0)
            {
                parentDirectory += GVFSConstants.GitPathSeparator;
            }

            if (parentDirectory.StartsWith(GVFSConstants.GitPathSeparatorString))
            {
                parentDirectory = parentDirectory.TrimStart(GVFSConstants.GitPathSeparator);
            }

            gitTrackedFilesCount += this.CategorizePaths(this.gitPaths, gitTrackedFilesDirectoryTally, parentDirectory, isFile: true);
            placeholderCount += this.CategorizePaths(this.placeholderFolderPaths, hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            placeholderCount += this.CategorizePaths(this.placeholderFilePaths, hydratedFilesDirectoryTally, parentDirectory, isFile: true);
            modifiedPathsCount += this.CategorizePaths(this.modifiedFolderPaths, hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            modifiedPathsCount += this.CategorizePaths(this.modifiedFilePaths, hydratedFilesDirectoryTally, parentDirectory, isFile: true);

            Dictionary<string, decimal> mostHydratedDirectories = new Dictionary<string, decimal>();

            // Map directory names to the corresponding health data from gitTrackedFilesDirectoryTally and hydratedFilesDirectoryTally
            foreach (KeyValuePair<string, int> pair in gitTrackedFilesDirectoryTally)
            {
                if (hydratedFilesDirectoryTally.TryGetValue(pair.Key, out int hydratedFiles))
                {
                    // In-lining this for now until a better "health" calculation is created
                    // Another possibility is the ability to pass a function to use for health (might not be applicable)
                    mostHydratedDirectories.Add(pair.Key, (decimal)hydratedFiles / (decimal)pair.Value);
                }
                else
                {
                    mostHydratedDirectories.Add(pair.Key, 0);
                }
            }

            // The list of directories which will be passed into the data object
            List<ValueTuple<string, decimal>> outputDirectories = new List<ValueTuple<string, decimal>>();

            // Create a list of the most hydrated directories in sorted order of specified length to return
            foreach (KeyValuePair<string, decimal> pair in mostHydratedDirectories
                .OrderByDescending(kp => kp.Value)
                .ToList<KeyValuePair<string, decimal>>())
            {
                if (outputDirectories.Count < directoryOutputCount)
                {
                    outputDirectories.Add((pair.Key, pair.Value));
                }
            }

            // Generate and return a data object
            return new GVFSEnlistmentHealthData(parentDirectory, gitTrackedFilesCount, placeholderCount, modifiedPathsCount, outputDirectories);
        }

        public decimal GetPlaceholderPercentage(GVFSEnlistmentHealthData healthData)
        {
            if (healthData.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (decimal)healthData.PlaceholderCount / healthData.GitTrackedFilesCount;
        }

        public decimal GetModifiedPathsPercentage(GVFSEnlistmentHealthData healthData)
        {
            if (healthData.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (decimal)healthData.ModifiedPathsCount / healthData.GitTrackedFilesCount;
        }

        /// <summary>
        /// Take a string representing a file path on system and pull out the upper-most directory from it as a string, or GVFSConstants.GitPathSeparator if it is in the root
        /// </summary>
        /// <param name="path">The path to a file to parse for the top level directory containing it</param>
        /// <returns>A string containing the top level directory from the provided path, or GVFSConstants.GitPathSeparator if the path is for an item in the root</returns>
        private string ParseTopDirectory(string path)
        {
            int whackLocation = path.IndexOf(GVFSConstants.GitPathSeparator);
            if (whackLocation == -1)
            {
                return GVFSConstants.GitPathSeparatorString;
            }

            return path.Substring(0, whackLocation);
        }

        /// <summary>
        /// Categorizes a list of paths given as strings by mapping them to the top level directory in their path
        /// Modifies the directoryTracking dictionary to have an accurate count of the files underneath a top level directory
        /// </summary>
        /// <remarks>
        /// The distinction between files and directories is important --
        /// If the path to a file doesn't contain a GVFSConstants.GitPathSeparator, then that means it falls within the root
        /// However if a directory's path doesn't contain a GVFSConstants.GitPathSeparator, it counts towards itself for hydration
        /// </remarks>
        /// <param name="paths">An enumerable containing paths as strings</param>
        /// <param name="directoryTracking">A dictionary used to track the number of files per top level directory</param>
        /// <param name="parentDirectory">Paths will only be categorized if they are descendants of the parentDirectory</param>
        /// <param name="isFile">Flag which specifies whether the list of paths are paths to files (true) or directories (false)</param>
        private int CategorizePaths(IEnumerable<string> paths, Dictionary<string, int> directoryTracking, string parentDirectory, bool isFile)
        {
            int count = 0;
            foreach (string path in paths)
            {
                // Ensure all paths have the same slashes
                string formattedPath = path.Replace('\\', GVFSConstants.GitPathSeparator);
                if (formattedPath.StartsWith(GVFSConstants.GitPathSeparator.ToString()))
                {
                    formattedPath = formattedPath.Substring(1);
                }

                // Only categorize if descendent of the parentDirectory
                if (formattedPath.StartsWith(parentDirectory))
                {
                    count++;

                    if (isFile)
                    {
                        // Find out the top level directory of the files path, which will be one level under the parent directory
                        // If the file is in the parent directory, topDir is set to '/' (just temporary, formatting will eventually just show the path to the parent directory)
                        string topDir = this.ParseTopDirectory(this.TrimDirectoryFromPath(formattedPath, parentDirectory));
                        this.IncreaseDictionaryCounterByKey(directoryTracking, topDir);
                    }
                    else
                    {
                        // If the path is to the parentDirectory, ignore it to avoid adding string.Empty to the data structures
                        if (!parentDirectory.Equals(formattedPath))
                        {
                            // Trim the path to parent directory from the path to this directory
                            string topDir = this.TrimDirectoryFromPath(formattedPath, parentDirectory);

                            // If this directory isn't already one level under the parent...
                            if (topDir.IndexOf(GVFSConstants.GitPathSeparator) != -1)
                            {
                                // ... Get the one that is
                                topDir = this.ParseTopDirectory(topDir);
                            }

                            this.IncreaseDictionaryCounterByKey(directoryTracking, topDir);
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Trim the relative path to a directory from the front of a specified path which is its child
        /// </summary>
        /// <remarks>Precondition: Only even run in the context of TrimDirectoryFromPath(child, parentDirectory)</remarks>
        /// <param name="path">The path being trimmed</param>
        /// <param name="directoryTarget">The directory target whose path to trim from the path</param>
        /// <returns>The newly formatted path with the directory trimmed</returns>
        private string TrimDirectoryFromPath(string path, string directoryTarget)
        {
            return path.Substring(directoryTarget.Length);
        }

        private void IncreaseDictionaryCounterByKey(Dictionary<string, int> countingDictionary, string key)
        {
            if (!countingDictionary.TryGetValue(key, out int count))
            {
                count = 0;
            }

            countingDictionary[key] = ++count;
        }

        public class GVFSEnlistmentHealthData
        {
            public GVFSEnlistmentHealthData(
                string targetDirectory,
                int gitFilesCount,
                int placeholderCount,
                int modifiedPathsCount,
                List<ValueTuple<string, decimal>> directoryHydrationLevels)
            {
                this.TargetDirectory = targetDirectory;
                this.GitTrackedFilesCount = gitFilesCount;
                this.PlaceholderCount = placeholderCount;
                this.ModifiedPathsCount = modifiedPathsCount;
                this.DirectoryHydrationLevels = directoryHydrationLevels;
            }

            public string TargetDirectory { get; private set; }
            public int GitTrackedFilesCount { get; private set; }
            public int PlaceholderCount { get; private set; }
            public int ModifiedPathsCount { get; private set; }
            public List<ValueTuple<string, decimal>> DirectoryHydrationLevels { get; private set; }
        }
    }
}
