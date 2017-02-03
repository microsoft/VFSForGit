using GVFS.Common;
using System;
using System.Linq;

namespace GVFS.GVFlt
{
    public class PathUtil
    {
        /// <summary>
        /// Returns true for paths that begin with ".git\" (regardless of case)
        /// </summary>
        public static bool IsPathInsideDotGit(string virtualPath)
        {
            return virtualPath.StartsWith(GVFSConstants.DotGit.Root + GVFSConstants.PathSeparator, StringComparison.OrdinalIgnoreCase);
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
