using System.IO;
using GVFS.Common;

namespace GVFS.Common.Prefetch
{
    public static class PathConverter
    {
        public static string FromWindowsFullPathToGitRelativePath(this string path, string repoRoot)
        {
            return path.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
        }

        public static string FromGitRelativePathToWindowsFullPath(this string path, string repoRoot)
        {
            return Path.Combine(repoRoot, path.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar));
        }
    }
}
