using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using System;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    public class EnlistmentHydrationSummary
    {
        public int HydratedFileCount { get; private set; }
        public int TotalFileCount { get; private set; }
        public int HydratedFolderCount { get; private set; }
        public int TotalFolderCount { get; private set; }

        public bool IsValid
        {
            get
            {
                return HydratedFileCount >= 0
                && HydratedFolderCount >= 0
                && TotalFileCount >= HydratedFileCount
                && TotalFolderCount >= HydratedFolderCount;
            }
        }

        public string ToMessage()
        {
            if (!IsValid)
            {
                return "Error calculating hydration. Run 'gvfs health' for details.";
            }

            int fileHydrationPercent = TotalFileCount == 0 ? 0 : (100 * HydratedFileCount) / TotalFileCount;
            int folderHydrationPercent = TotalFolderCount == 0 ? 0 : ((100 * HydratedFolderCount) / TotalFolderCount);
            return $"{fileHydrationPercent}% of files and {folderHydrationPercent}% of folders hydrated. Run 'gvfs health' for details.";
        }

        public static EnlistmentHydrationSummary CreateSummary(
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem)
        {
            try
            {
                /* Getting all the file paths from git index is slow and we only need the total count,
                 * so we read the index file header instead of calling GetPathsFromGitIndex */
                int totalFileCount = GetIndexFileCount(enlistment, fileSystem);

                /* Getting all the directories is also slow, but not as slow as reading the entire index,
                 * GetTotalPathCount caches the count so this is only slow occasionally,
                 * and the GitStatusCache manager also calls this to ensure it is updated frequently. */
                int totalFolderCount = GetHeadTreeCount(enlistment, fileSystem);

                EnlistmentPathData pathData = new EnlistmentPathData();

                /* FUTURE: These could be optimized to only deal with counts instead of full path lists */
                pathData.LoadPlaceholdersFromDatabase(enlistment);
                pathData.LoadModifiedPaths(enlistment);

                int hydratedFileCount = pathData.ModifiedFilePaths.Count + pathData.PlaceholderFilePaths.Count;
                int hydratedFolderCount = pathData.ModifiedFolderPaths.Count + pathData.PlaceholderFolderPaths.Count;
                return new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = hydratedFileCount,
                    HydratedFolderCount = hydratedFolderCount,
                    TotalFileCount = totalFileCount,
                    TotalFolderCount = totalFolderCount,
                };
            }
            catch
            {
                return new EnlistmentHydrationSummary()
                {
                    HydratedFileCount = -1,
                    HydratedFolderCount = -1,
                    TotalFileCount = -1,
                    TotalFolderCount = -1,
                };
            }
        }

        /// <summary>
        /// Get the total number of files in the index.
        /// </summary>
        internal static int GetIndexFileCount(GVFSEnlistment enlistment, PhysicalFileSystem fileSystem)
        {
            string indexPath = Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Index);
            using (var indexFile = fileSystem.OpenFileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, callFlushFileBuffers: false))
            {
                if (indexFile.Length < 12)
                {
                    return -1;
                }
                /* The number of files in the index is a big-endian integer from bytes 9-12 of the index file. */
                indexFile.Position = 8;
                var bytes = new byte[4];
                indexFile.Read(bytes, 0, 4);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                int count = BitConverter.ToInt32(bytes, 0);
                return count;
            }
        }

        /// <summary>
        /// Get the total number of trees in the repo at HEAD.
        /// </summary>
        /// <remarks>
        /// This is used as the denominator in displaying percentage of hydrated
        /// directories as part of git status pre-command hook.
        /// It can take several seconds to calculate, so we cache it near the git status cache.
        /// </remarks>
        /// <returns>
        /// The number of subtrees at HEAD, which may be 0.
        /// Will return 0 if unsuccessful.
        /// </returns>
        internal static int GetHeadTreeCount(GVFSEnlistment enlistment, PhysicalFileSystem fileSystem)
        {
            var gitProcess = enlistment.CreateGitProcess();
            var headResult = gitProcess.GetHeadTreeId();
            if (headResult.ExitCodeIsFailure)
            {
                return 0;
            }
            var headSha = headResult.Output.Trim();
            var cacheFile = Path.Combine(
                enlistment.DotGVFSRoot,
                GVFSConstants.DotGVFS.GitStatusCache.TreeCount);

            // Load from cache if cache matches current HEAD.
            if (fileSystem.FileExists(cacheFile))
            {
                try
                {
                    var lines = fileSystem.ReadLines(cacheFile).ToArray();
                    if (lines.Length == 2
                        && lines[0] == headSha
                        && int.TryParse(lines[1], out int cachedCount))
                    {
                        return cachedCount;
                    }
                }
                catch
                {
                    // Ignore errors reading the cache
                }
            }

            int totalPathCount = 0;
            GitProcess.Result folderResult = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line => totalPathCount++,
                recursive: true,
                showDirectories: true);
            try
            {
                fileSystem.CreateDirectory(Path.GetDirectoryName(cacheFile));
                fileSystem.WriteAllText(cacheFile, $"{headSha}\n{totalPathCount}");
            }
            catch
            {
                // Ignore errors writing the cache
            }

            return totalPathCount;
        }
    }
}
