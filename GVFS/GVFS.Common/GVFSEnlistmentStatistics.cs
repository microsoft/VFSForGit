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

            if (!parentDirectory.EndsWith(GVFSConstants.GitPathSeparatorString) && parentDirectory.Length > 0)
            {
                parentDirectory += GVFSConstants.GitPathSeparator;
            }

            if (parentDirectory.StartsWith(GVFSConstants.GitPathSeparatorString))
            {
                parentDirectory = parentDirectory.TrimStart(GVFSConstants.GitPathSeparator);
            }

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

            directoriesSortedByHydration = directoriesSortedByHydration.OrderByDescending(path => this.GetHydrationOfDirectory(path)).ToList();

            return directoriesSortedByHydration;
        }

        public decimal GetHydrationOfDirectory(string directory)
        {
            if (!this.hydratedFilesDirectoryTally.ContainsKey(directory))
            {
                return 0;
            }

            return (decimal)this.hydratedFilesDirectoryTally[directory] / this.gitTrackedFilesDirectoryTally[directory];
        }

        public decimal GetPlaceholderPercentage()
        {
            if (this.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (decimal)this.PlaceholderCount / this.GitTrackedFilesCount;
        }

        public decimal GetModifiedPathsPercentage()
        {
            if (this.GitTrackedFilesCount == 0)
            {
                return 0;
            }

            return (decimal)this.ModifiedPathsCount / this.GitTrackedFilesCount;
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
    }
}
