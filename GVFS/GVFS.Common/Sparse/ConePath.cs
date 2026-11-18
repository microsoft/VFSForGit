using System;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Path normalization shared by <see cref="ConeBuilder"/> and <see cref="ConePathMatcher"/>.
    /// </summary>
    internal static class ConePath
    {
        /// <summary>
        /// Convert a repo-relative path to git form: forward slashes, no leading or trailing
        /// separator, and no duplicate separators. A trailing separator on the input marks a
        /// directory. An empty result is the repository root.
        /// </summary>
        public static string Normalize(string rawPath, out bool isDirectory)
        {
            string forwardSlashed = rawPath.Replace('\\', GVFSConstants.GitPathSeparator);
            isDirectory = forwardSlashed.EndsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal);

            string[] parts = forwardSlashed.Split(GVFSConstants.GitPathSeparator, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(GVFSConstants.GitPathSeparatorString, parts);
        }

        /// <summary>
        /// Return the parent directory of a git-form path, or an empty string for a path at
        /// the repository root.
        /// </summary>
        public static string GetParent(string path)
        {
            int lastSeparator = path.LastIndexOf(GVFSConstants.GitPathSeparator);
            return lastSeparator < 0 ? string.Empty : path.Substring(0, lastSeparator);
        }
    }
}
