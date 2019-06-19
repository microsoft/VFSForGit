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
        private readonly GVFSEnlistmentPathData enlistmentPathData;

        public GVFSEnlistmentHealthCalculator(GVFSEnlistmentPathData pathData)
        {
            this.enlistmentPathData = pathData;
        }

        public GVFSEnlistmentHealthData CalculateStatistics(string parentDirectory, int directoryOutputCount)
        {
            int gitTrackedItemsCount = 0;
            int placeholderCount = 0;
            int modifiedPathsCount = 0;
            Dictionary<string, int> gitTrackedItemsDirectoryTally = new Dictionary<string, int>();
            Dictionary<string, int> hydratedFilesDirectoryTally = new Dictionary<string, int>();

            if (!parentDirectory.EndsWith(GVFSConstants.GitPathSeparatorString) && parentDirectory.Length > 0)
            {
                parentDirectory += GVFSConstants.GitPathSeparator;
            }

            if (parentDirectory.StartsWith(GVFSConstants.GitPathSeparatorString))
            {
                parentDirectory = parentDirectory.TrimStart(GVFSConstants.GitPathSeparator);
            }

            gitTrackedItemsCount += this.CategorizePaths(this.enlistmentPathData.GitFolderPaths, gitTrackedItemsDirectoryTally, parentDirectory, isFile: false);
            gitTrackedItemsCount += this.CategorizePaths(this.enlistmentPathData.GitFilePaths, gitTrackedItemsDirectoryTally, parentDirectory, isFile: true);
            placeholderCount += this.CategorizePaths(this.enlistmentPathData.PlaceholderFolderPaths, hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            placeholderCount += this.CategorizePaths(this.enlistmentPathData.PlaceholderFilePaths, hydratedFilesDirectoryTally, parentDirectory, isFile: true);
            modifiedPathsCount += this.CategorizePaths(this.enlistmentPathData.ModifiedFolderPaths, hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            modifiedPathsCount += this.CategorizePaths(this.enlistmentPathData.ModifiedFilePaths, hydratedFilesDirectoryTally, parentDirectory, isFile: true);

            Dictionary<string, decimal> mostHydratedDirectories = new Dictionary<string, decimal>();

            // Map directory names to the corresponding health data from gitTrackedFilesDirectoryTally and hydratedFilesDirectoryTally
            foreach (KeyValuePair<string, int> pair in gitTrackedItemsDirectoryTally)
            {
                if (hydratedFilesDirectoryTally.TryGetValue(pair.Key, out int hydratedFiles))
                {
                    // In-lining this for now until a better "health" calculation is created
                    // Another possibility is the ability to pass a function to use for health (might not be applicable)
                    mostHydratedDirectories.Add(pair.Key, this.CalculateHealthMetric(hydratedFiles, pair.Value));
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
                .OrderByDescending(kp => kp.Value))
            {
                if (outputDirectories.Count < directoryOutputCount)
                {
                    outputDirectories.Add((pair.Key, pair.Value));
                }
            }

            // Generate and return a data object
            return new GVFSEnlistmentHealthData(parentDirectory, gitTrackedItemsCount, placeholderCount, modifiedPathsCount, outputDirectories);
        }

        public decimal GetPlaceholderPercentage(GVFSEnlistmentHealthData healthData)
        {
            if (healthData.GitTrackedItemsCount == 0)
            {
                return 0;
            }

            return (decimal)healthData.PlaceholderCount / healthData.GitTrackedItemsCount;
        }

        public decimal GetModifiedPathsPercentage(GVFSEnlistmentHealthData healthData)
        {
            if (healthData.GitTrackedItemsCount == 0)
            {
                return 0;
            }

            return (decimal)healthData.ModifiedPathsCount / healthData.GitTrackedItemsCount;
        }

        public decimal CalculateHealthMetric(GVFSEnlistmentHealthData healthData)
        {
            return this.CalculateHealthMetric(healthData.PlaceholderCount + healthData.ModifiedPathsCount, healthData.GitTrackedItemsCount);
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
        /// However if a directory's path doesn't contain a GVFSConstants.GitPathSeparator, it doesn't count towards its own hydration
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
                // Only categorize if descendent of the parentDirectory
                if (path.StartsWith(parentDirectory))
                {
                    count++;

                    if (isFile)
                    {
                        // Find out the top level directory of the files path, which will be one level under the parent directory
                        // If the file is in the parent directory, topDir is set to '/' (just temporary, formatting will eventually just show the path to the parent directory)
                        string topDir = this.ParseTopDirectory(this.TrimDirectoryFromPath(path, parentDirectory));
                        this.IncreaseDictionaryCounterByKey(directoryTracking, topDir);
                    }
                    else
                    {
                        // If the path is to the parentDirectory, ignore it to avoid adding string.Empty to the data structures
                        if (!parentDirectory.Equals(path))
                        {
                            // Trim the path to parent directory from the path to this directory
                            string topDir = this.TrimDirectoryFromPath(path, parentDirectory);

                            // If the directory isn't one level underneath the parent, count it
                            // Otherwise, don't add it to the dictionary (it shouldn't count itself)
                            if (topDir.IndexOf(GVFSConstants.GitPathSeparator) != -1)
                            {
                                topDir = this.ParseTopDirectory(topDir);
                                this.IncreaseDictionaryCounterByKey(directoryTracking, topDir);
                            }
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

        private decimal CalculateHealthMetric(int hydratedFileCount, int totalFileCount)
        {
            return (decimal)hydratedFileCount / (decimal)totalFileCount;
        }

        public class GVFSEnlistmentPathData
        {
            public readonly List<string> GitFolderPaths;
            public readonly List<string> GitFilePaths;
            public readonly List<string> PlaceholderFolderPaths;
            public readonly List<string> PlaceholderFilePaths;
            public readonly List<string> ModifiedFolderPaths;
            public readonly List<string> ModifiedFilePaths;

            public GVFSEnlistmentPathData(
            List<string> gitFolderPaths,
            List<string> gitFilePaths,
            List<string> placeholderFolderPaths,
            List<string> placeholderFilePaths,
            List<string> modifiedFolderPaths,
            List<string> modifiedFilePaths,
            List<string> skipWorkTreeFilesPaths)
            {
                this.GitFolderPaths = gitFolderPaths;
                this.GitFilePaths = gitFilePaths;
                this.PlaceholderFolderPaths = placeholderFolderPaths;
                this.PlaceholderFilePaths = placeholderFilePaths;
                this.ModifiedFolderPaths = modifiedFolderPaths;
                this.ModifiedFilePaths = modifiedFilePaths;
                this.ModifiedFilePaths.Union(skipWorkTreeFilesPaths);

                this.NormalizePaths(this.GitFolderPaths);
                this.NormalizePaths(this.GitFilePaths);
                this.NormalizePaths(this.PlaceholderFolderPaths);
                this.NormalizePaths(this.PlaceholderFilePaths);
                this.NormalizePaths(this.ModifiedFolderPaths);
                this.NormalizePaths(this.ModifiedFilePaths);
            }

            private void NormalizePaths(List<string> paths)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    paths[i] = paths[i].Replace('\\', GVFSConstants.GitPathSeparator);
                    paths[i] = paths[i].TrimStart(GVFSConstants.GitPathSeparator);
                    paths[i] = paths[i].TrimEnd(GVFSConstants.GitPathSeparator);
                }
            }
        }

        public class GVFSEnlistmentHealthData
        {
            public GVFSEnlistmentHealthData(
                string targetDirectory,
                int gitItemsCount,
                int placeholderCount,
                int modifiedPathsCount,
                List<ValueTuple<string, decimal>> directoryHydrationLevels)
            {
                this.TargetDirectory = targetDirectory;
                this.GitTrackedItemsCount = gitItemsCount;
                this.PlaceholderCount = placeholderCount;
                this.ModifiedPathsCount = modifiedPathsCount;
                this.DirectoryHydrationLevels = directoryHydrationLevels;
            }

            public string TargetDirectory { get; private set; }
            public int GitTrackedItemsCount { get; private set; }
            public int PlaceholderCount { get; private set; }
            public int ModifiedPathsCount { get; private set; }
            public List<ValueTuple<string, decimal>> DirectoryHydrationLevels { get; private set; }
        }
    }
}
