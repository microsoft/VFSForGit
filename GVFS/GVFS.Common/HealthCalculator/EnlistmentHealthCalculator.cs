using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Class responsible for the business logic involved in calculating the health statistics
    /// of a gvfs enlistment. Constructed with the lists of paths for the enlistment, and then
    /// internally stores the calculated information. Compute or recompute via CalculateStatistics
    /// with an optional parameter to only look for paths which are under the specified directory
    /// </summary>
    public class EnlistmentHealthCalculator
    {
        // In the context of this class, hydrated files are placeholders or modified paths
        // The total number of hydrated files is this.PlaceholderCount + this.ModifiedPathsCount
        private readonly EnlistmentPathData enlistmentPathData;

        public EnlistmentHealthCalculator(EnlistmentPathData pathData)
        {
            this.enlistmentPathData = pathData;
        }

        public EnlistmentHealthData CalculateStatistics(string parentDirectory)
        {
            int gitTrackedItemsCount = 0;
            int placeholderCount = 0;
            int modifiedPathsCount = 0;
            Dictionary<string, int> gitTrackedItemsDirectoryTally = new Dictionary<string, int>(GVFSPlatform.Instance.Constants.PathComparer);
            Dictionary<string, int> hydratedFilesDirectoryTally = new Dictionary<string, int>(GVFSPlatform.Instance.Constants.PathComparer);

            // Parent directory is a path relative to the root of the repository which is already in git format
            if (!parentDirectory.EndsWith(GVFSConstants.GitPathSeparatorString) && parentDirectory.Length > 0)
            {
                parentDirectory += GVFSConstants.GitPathSeparator;
            }

            if (parentDirectory.StartsWith(GVFSConstants.GitPathSeparatorString))
            {
                parentDirectory = parentDirectory.TrimStart(GVFSConstants.GitPathSeparator);
            }

            gitTrackedItemsCount += this.CategorizePaths(this.enlistmentPathData.GitFolderPaths, gitTrackedItemsDirectoryTally, parentDirectory);
            gitTrackedItemsCount += this.CategorizePaths(this.enlistmentPathData.GitFilePaths, gitTrackedItemsDirectoryTally, parentDirectory);
            placeholderCount += this.CategorizePaths(this.enlistmentPathData.PlaceholderFolderPaths, hydratedFilesDirectoryTally, parentDirectory);
            placeholderCount += this.CategorizePaths(this.enlistmentPathData.PlaceholderFilePaths, hydratedFilesDirectoryTally, parentDirectory);
            modifiedPathsCount += this.CategorizePaths(this.enlistmentPathData.ModifiedFolderPaths, hydratedFilesDirectoryTally, parentDirectory);
            modifiedPathsCount += this.CategorizePaths(this.enlistmentPathData.ModifiedFilePaths, hydratedFilesDirectoryTally, parentDirectory);

            Dictionary<string, SubDirectoryInfo> mostHydratedDirectories = new Dictionary<string, SubDirectoryInfo>(GVFSPlatform.Instance.Constants.PathComparer);

            // Map directory names to the corresponding health data from gitTrackedItemsDirectoryTally and hydratedFilesDirectoryTally
            foreach (KeyValuePair<string, int> pair in gitTrackedItemsDirectoryTally)
            {
                if (hydratedFilesDirectoryTally.TryGetValue(pair.Key, out int hydratedFiles))
                {
                    // In-lining this for now until a better "health" calculation is created
                    // Another possibility is the ability to pass a function to use for health (might not be applicable)
                    mostHydratedDirectories.Add(pair.Key, new SubDirectoryInfo(pair.Key, hydratedFiles, pair.Value));
                }
                else
                {
                    mostHydratedDirectories.Add(pair.Key, new SubDirectoryInfo(pair.Key, 0, pair.Value));
                }
            }

            return new EnlistmentHealthData(
                parentDirectory,
                gitTrackedItemsCount,
                placeholderCount,
                modifiedPathsCount,
                this.CalculateHealthMetric(placeholderCount + modifiedPathsCount, gitTrackedItemsCount),
                mostHydratedDirectories.OrderByDescending(kp => kp.Value.HydratedFileCount).Select(item => item.Value).ToList());
        }

        /// <summary>
        /// Take a file path and get the top level directory from it, or GVFSConstants.GitPathSeparator if it is not in a directory
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
        private int CategorizePaths(IEnumerable<string> paths, Dictionary<string, int> directoryTracking, string parentDirectory)
        {
            int count = 0;
            foreach (string path in paths)
            {
                // Only categorize if descendent of the parentDirectory
                if (path.StartsWith(parentDirectory, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    count++;

                    // If the path is to the parentDirectory, ignore it to avoid adding string.Empty to the data structures
                    if (!parentDirectory.Equals(path, GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        // Trim the path to parent directory
                        string topDir = this.ParseTopDirectory(this.TrimDirectoryFromPath(path, parentDirectory));
                        if (!topDir.Equals(GVFSConstants.GitPathSeparatorString))
                        {
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
        /// <remarks>Precondition: 'directoryTarget' must be an ancestor of 'path'</remarks>
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
            if (totalFileCount == 0)
            {
                return 0;
            }

            return (decimal)hydratedFileCount / (decimal)totalFileCount;
        }

        public class SubDirectoryInfo
        {
            public SubDirectoryInfo(string name, int hydratedFileCount, int totalFileCount)
            {
                this.Name = name;
                this.HydratedFileCount = hydratedFileCount;
                this.TotalFileCount = totalFileCount;
            }

            public string Name { get; private set; }
            public int HydratedFileCount { get; private set; }
            public int TotalFileCount { get; private set; }
        }
    }
}
