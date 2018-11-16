using System;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    public static class Paths
    {
        public static string GetGitEnlistmentRoot(string directory)
        {
            return GetRoot(directory, GVFSConstants.DotGit.Root);
        }

        public static string GetServiceDataRoot(string serviceName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "GVFS",
                serviceName);
        }

        public static string GetServiceLogsPath(string serviceName)
        {
            return Path.Combine(GetServiceDataRoot(serviceName), "Logs");
        }

        public static string GetRoot(string startingDirectory, string rootName)
        {
            startingDirectory = startingDirectory.TrimEnd(Path.DirectorySeparatorChar);
            DirectoryInfo dirInfo;

            try
            {
                dirInfo = new DirectoryInfo(startingDirectory);
            }
            catch (Exception)
            {
                return null;
            }

            while (dirInfo != null)
            {
                if (dirInfo.Exists)
                {
                    DirectoryInfo[] dotGVFSDirs = new DirectoryInfo[0];

                    try
                    {
                        dotGVFSDirs = dirInfo.GetDirectories(rootName);
                    }
                    catch (IOException)
                    {
                    }

                    if (dotGVFSDirs.Count() == 1)
                    {
                        return dirInfo.FullName;
                    }
                }

                dirInfo = dirInfo.Parent;
            }

            return null;
        }

        public static string ConvertPathToGitFormat(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
        }

        public static string MakeRelative(string rootPath, string path)
        {
            // Ensure trailing slash on the rootPath
            if (rootPath.Length > 0 && rootPath.Last() != Path.DirectorySeparatorChar)
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            Uri rootUri = new Uri(rootPath, UriKind.Absolute);
            Uri pathUri = new Uri(path, UriKind.Absolute);

            Uri relativeUri = rootUri.MakeRelativeUri(pathUri);

            string relativePath = relativeUri.ToString();

            // Convert to native path separators
            return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }
}
