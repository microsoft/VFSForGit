using GVFS.Common.Database;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    /// <summary>
    /// Class responsible for the business logic involved in calculating the health statistics
    /// of a gvfs enlistment. Constructed with the lists of paths for the enlistment, and then
    /// internally stores the calculated information. Compute or recompute via CalculateStatistics
    /// with an optional parameter to only look for paths which are under the specified directory
    /// </summary>
    public class GVFSEnlistmentStatistics
    {
        // In the context of this project, hydrated files are placeholders or modified paths
        // The total number of hydrated files is this.PlaceholderCount + this.ModifiedPathsCount
        private List<string> gitPaths;
        private List<string> placeholderFolderPaths;
        private List<string> placeholderFilePaths;
        private List<string> modifiedFolderPaths;
        private List<string> modifiedFilePaths;
        private Dictionary<string, int> gitTrackedFilesDirectoryTally;
        private Dictionary<string, int> hydratedFilesDirectoryTally;

        public GVFSEnlistmentStatistics(
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

        public int GitTrackedFilesCount { get; private set; }
        public int PlaceholderCount { get; private set; }
        public int ModifiedPathsCount { get; private set; }

        public void CalculateStatistics(string parentDirectory)
        {
            this.GitTrackedFilesCount = 0;
            this.PlaceholderCount = 0;
            this.ModifiedPathsCount = 0;
            this.gitTrackedFilesDirectoryTally = new Dictionary<string, int>();
            this.hydratedFilesDirectoryTally = new Dictionary<string, int>();

            this.GitTrackedFilesCount += this.CategorizePaths(this.gitPaths, this.gitTrackedFilesDirectoryTally, parentDirectory, isFile: true);
            this.PlaceholderCount += this.CategorizePaths(this.placeholderFolderPaths, this.hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            this.PlaceholderCount += this.CategorizePaths(this.placeholderFilePaths, this.hydratedFilesDirectoryTally, parentDirectory, isFile: true);
            this.ModifiedPathsCount += this.CategorizePaths(this.modifiedFolderPaths, this.hydratedFilesDirectoryTally, parentDirectory, isFile: false);
            this.ModifiedPathsCount += this.CategorizePaths(this.modifiedFilePaths, this.hydratedFilesDirectoryTally, parentDirectory, isFile: true);
        }

        public List<string> GetDirectoriesSortedByHydration()
        {
            List<string> directoriesSortedByHydration = new List<string>();

            foreach (KeyValuePair<string, int> keyValuePair in this.gitTrackedFilesDirectoryTally)
            {
                directoriesSortedByHydration.Add(keyValuePair.Key);
            }

            directoriesSortedByHydration.Sort(
                delegate(string directoryOne, string directoryTwo)
                {
                    // Sort in descending order by comparing directoryTwo to directoryOne
                    return this.GetHydrationOfDirectory(directoryTwo).CompareTo(this.GetHydrationOfDirectory(directoryOne));
                });

            return directoriesSortedByHydration;
        }

        public double GetHydrationOfDirectory(string directory)
        {
            if (!this.hydratedFilesDirectoryTally.ContainsKey(directory))
            {
                return 0;
            }

            return (double)this.hydratedFilesDirectoryTally[directory] / this.gitTrackedFilesDirectoryTally[directory];
        }

        public double GetPlaceholderPercentage()
        {
            if (this.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (double)this.PlaceholderCount / this.GitTrackedFilesCount;
        }

        public double GetModifiedPathsPercentage()
        {
            if (this.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (double)this.ModifiedPathsCount / this.GitTrackedFilesCount;
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
                return GVFSConstants.GitPathSeparator.ToString();
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

                if (formattedPath.StartsWith(parentDirectory))
                {
                    count++;

                    if (isFile)
                    {
                        string topDir = this.ParseTopDirectory(this.TrimDirectoryFromPath(formattedPath, parentDirectory));
                        this.IncreaseDictionaryCounterByKey(directoryTracking, topDir);
                    }
                    else
                    {
                        if (!parentDirectory.Equals(formattedPath))
                        {
                            string topDir = this.TrimDirectoryFromPath(formattedPath, parentDirectory);
                            if (topDir.IndexOf(GVFSConstants.GitPathSeparator) != -1)
                            {
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
        /// Format a given path by trimming off the leading part containing the target directory
        /// </summary>
        /// <param name="toTrim">The path to trim the original directory off of</param>
        /// <returns>The newly formatted path without that initial directory</returns>
        private string TrimDirectoryFromPath(string path, string directoryTarget)
        {
            if (directoryTarget.Length == 0)
            {
                return path;
            }
            else
            {
                return path.Substring(directoryTarget.Length + 1);
            }
        }

        private void IncreaseDictionaryCounterByKey(Dictionary<string, int> countingDictionary, string key)
        {
            if (!countingDictionary.TryGetValue(key, out int count))
            {
                count = 0;
            }

            countingDictionary[key] = ++count;
        }
    }
}
