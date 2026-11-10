using System;
using System.IO;

namespace GVFS.UnitTests
{
    internal static class GitTestHelper
    {
        internal static string FindGitOnPath()
        {
            return FindGitOnPath(Environment.GetEnvironmentVariable("PATH"));
        }

        internal static string FindGitOnPath(string searchPath)
        {
            if (string.IsNullOrEmpty(searchPath))
            {
                return null;
            }

            foreach (string entry in searchPath.Split(Path.PathSeparator))
            {
                string directory = entry.Trim('"');
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string gitPath = Path.Combine(directory, "git.exe");
                if (File.Exists(gitPath))
                {
                    return Path.GetFullPath(gitPath);
                }
            }

            return null;
        }
    }
}
