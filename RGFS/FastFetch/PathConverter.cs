using System.IO;
using RGFS.Common;

namespace FastFetch
{
    public static class PathConverter
    {
        public static string FromWindowsFullPathToGitRelativePath(this string path, string repoRoot)
        {
            return path.Substring(repoRoot.Length).TrimStart(RGFSConstants.PathSeparator).Replace(RGFSConstants.PathSeparator, RGFSConstants.GitPathSeparator);
        }

        public static string FromGitRelativePathToWindowsFullPath(this string path, string repoRoot)
        {
            return Path.Combine(repoRoot, path.Replace(RGFSConstants.GitPathSeparator, RGFSConstants.PathSeparator));
        }
    }
}
