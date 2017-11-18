using RGFS.Common;
using System;

namespace RGFS.GVFlt
{
    public class PathUtil
    {
        /// <summary>
        /// Returns true for paths that begin with ".git\" (regardless of case)
        /// </summary>
        public static bool IsPathInsideDotGit(string virtualPath)
        {
            return virtualPath.StartsWith(RGFSConstants.DotGit.Root + RGFSConstants.PathSeparator, StringComparison.OrdinalIgnoreCase);
        }

        public static string RemoveTrailingSlashIfPresent(string path)
        {
            return path.TrimEnd('\\');
        }

        public static bool IsEnumerationFilterSet(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter == "*")
            {
                return false;
            }

            return true;
        }
    }
}
