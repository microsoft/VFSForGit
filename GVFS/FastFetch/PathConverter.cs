using System.IO;
using GVFS.Common;

namespace FastFetch
{
    public static class PathConverter
    {
        public static string FromWindowsFullPathToGitRelativePath(this string path, string repoRoot)
        {
            return path.Substring(repoRoot.Length).TrimStart(GVFSConstants.PathSeparator).Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator);
        }

        public static string FromGitRelativePathToWindowsFullPath(this string path, string repoRoot)
        {
            return Path.Combine(repoRoot, path.Replace(GVFSConstants.GitPathSeparator, GVFSConstants.PathSeparator));
        }
    }
}
