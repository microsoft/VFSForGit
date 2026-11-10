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

            int entryStart = 0;
            bool quoted = false;
            for (int index = 0; index <= searchPath.Length; ++index)
            {
                if (index < searchPath.Length)
                {
                    if (searchPath[index] == '"')
                    {
                        quoted = !quoted;
                    }

                    if (searchPath[index] != Path.PathSeparator || quoted)
                    {
                        continue;
                    }
                }

                string directory = searchPath.Substring(entryStart, index - entryStart).Trim('"');
                entryStart = index + 1;
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
