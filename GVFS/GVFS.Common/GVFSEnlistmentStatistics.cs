using GVFS.Common.Database;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class GVFSEnlistmentStatistics
    {
        private List<string> gitPaths;
        private List<string> placeholderFolderPaths;
        private List<string> placeholderFilePaths;
        private List<string> modifiedFolderPaths;
        private List<string> modifiedFilePaths;
        private int trackedFilesCount;
        private int placeholderCount;
        private int modifiedPathsCount;
        private Dictionary<string, int> trackedFilesDirectoryTracking;
        private Dictionary<string, int> hydratedFilesDirectoryTracking;

        public GVFSEnlistmentStatistics(List<string> gitPaths, List<string> placeholderFolderPaths, List<string> placeholderFilePaths, List<string> modifiedFolderPaths, List<string> modifiedFilePaths)
        {
            this.gitPaths = gitPaths;
            this.placeholderFolderPaths = placeholderFolderPaths;
            this.placeholderFilePaths = placeholderFilePaths;
            this.modifiedFolderPaths = modifiedFolderPaths;
            this.modifiedFilePaths = modifiedFilePaths;
        }

        public void CalculateStatistics(string parentDirectory)
        {
            this.trackedFilesCount = 0;
            this.placeholderCount = 0;
            this.modifiedPathsCount = 0;
            this.trackedFilesDirectoryTracking = new Dictionary<string, int>();
            this.hydratedFilesDirectoryTracking = new Dictionary<string, int>();

            this.CategorizePaths(this.gitPaths, this.trackedFilesDirectoryTracking, ref this.trackedFilesCount, parentDirectory, isFile: true);
            this.CategorizePaths(this.placeholderFolderPaths, this.hydratedFilesDirectoryTracking, ref this.placeholderCount, parentDirectory, isFile: false);
            this.CategorizePaths(this.placeholderFilePaths, this.hydratedFilesDirectoryTracking, ref this.placeholderCount, parentDirectory, isFile: true);
            this.CategorizePaths(this.modifiedFolderPaths, this.hydratedFilesDirectoryTracking, ref this.modifiedPathsCount, parentDirectory, isFile: false);
            this.CategorizePaths(this.modifiedFilePaths, this.hydratedFilesDirectoryTracking, ref this.modifiedPathsCount, parentDirectory, isFile: true);
        }

        public List<string> GetDirectoriesSortedByHydration()
        {
            List<string> directoriesSortedByHydration = new List<string>();

            foreach (KeyValuePair<string, int> keyValuePair in this.trackedFilesDirectoryTracking)
            {
                directoriesSortedByHydration.Add(keyValuePair.Key);
            }

            directoriesSortedByHydration.Sort(
                delegate(string directoryOne, string directoryTwo)
                {
                    return this.GetHydrationOfDirectory(directoryOne).CompareTo(this.GetHydrationOfDirectory(directoryTwo)) * -1; // Multiply by -1 to reverse the sort
                });

            return directoriesSortedByHydration;
        }

        public double GetHydrationOfDirectory(string directory)
        {
            if (!this.hydratedFilesDirectoryTracking.ContainsKey(directory))
            {
                return 0;
            }

            return (double)this.hydratedFilesDirectoryTracking[directory] / this.trackedFilesDirectoryTracking[directory];
        }

        public int GetTrackedFilesCount()
        {
            return this.trackedFilesCount;
        }

        public int GetPlaceholderCount()
        {
            return this.placeholderCount;
        }

        public int GetModifiedPathsCount()
        {
            return this.modifiedPathsCount;
        }

        public double GetPlaceholderPercentage()
        {
            return (double)this.placeholderCount / this.trackedFilesCount;
        }

        public double GetModifiedPathsPercentage()
        {
            return (double)this.modifiedPathsCount / this.trackedFilesCount;
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
        private void CategorizePaths(IEnumerable<string> paths, Dictionary<string, int> directoryTracking, ref int count, string parentDirectory, bool isFile)
        {
            foreach (string path in paths)
            {
                // Ensure all paths have the same slashes
                string formattedPath = path.Replace('\\', '/');

                if (formattedPath.StartsWith(parentDirectory))
                {
                    count++;

                    if (isFile)
                    {
                        string topDir = this.ParseTopDirectory(this.TrimDirectoryTarget(formattedPath, parentDirectory));
                        directoryTracking[topDir] = directoryTracking.ContainsKey(topDir) ? directoryTracking[topDir] + 1 : 1;
                    }
                    else
                    {
                        if (!parentDirectory.Equals(formattedPath))
                        {
                            string topDir = this.TrimDirectoryTarget(formattedPath, parentDirectory);
                            if (topDir.IndexOf('/') != -1)
                            {
                                topDir = this.ParseTopDirectory(topDir);
                            }

                            directoryTracking[topDir] = directoryTracking.ContainsKey(topDir) ? directoryTracking[topDir] + 1 : 1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Format a given path by trimming off the leading part containing the target directory
        /// </summary>
        /// <param name="toTrim">The path to trim the original directory off of</param>
        /// <returns>The newly formatted path without that initial directory</returns>
        private string TrimDirectoryTarget(string path, string directoryTarget)
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
    }
}
