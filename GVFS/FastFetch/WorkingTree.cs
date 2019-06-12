using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GVFS.Common;

namespace FastFetch
{
    public static class WorkingTree
    {
        /// <summary>
        /// Enumerates all files in the working tree in a asynchronous parallel manner.
        /// Files found are sent to callback in chunks.
        /// Make no assumptions about ordering or how big chunks will be
        /// </summary>
        /// <param name="repoRoot"></param>
        /// <param name="asyncParallelCallback"></param>
        public static void ForAllFiles(string repoRoot, Action<string, FileInfo[]> asyncParallelCallback)
        {
            ForAllDirectories(new DirectoryInfo(repoRoot), asyncParallelCallback);
        }

        public static void ForAllDirectories(DirectoryInfo dir, Action<string, FileInfo[]> asyncParallelCallback)
        {
            asyncParallelCallback(dir.FullName, dir.GetFiles());

            Parallel.ForEach(
                dir.EnumerateDirectories().Where(subdir =>
                    (!subdir.Name.Equals(GVFSConstants.DotGit.Root, GVFSPlatform.Instance.Constants.PathComparison) &&
                     !subdir.Attributes.HasFlag(FileAttributes.ReparsePoint))),
                subdir => { ForAllDirectories(subdir, asyncParallelCallback); });
        }
    }
}
