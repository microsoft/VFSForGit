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

        public static string GetRelativePath(string relativeTo, string path)
        {
            if (!Path.IsPathRooted(relativeTo))
            {
                throw new ArgumentException("Path must be absolute.", nameof(relativeTo));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Path must be absolute.", nameof(path));
            }

            // Normalize paths
            relativeTo = Path.GetFullPath(relativeTo);
            path = Path.GetFullPath(path);

            // Handle calculation of relative paths to self
            if (StringComparer.OrdinalIgnoreCase.Equals(relativeTo, path))
            {
                return ".";
            }

            // For UNIX style paths we must prepend the "file://" scheme explicitly to create a System.Uri
            const char UnixPathRoot = '/';
            if (relativeTo.Length > 0 && relativeTo[0] == UnixPathRoot)
            {
                relativeTo = $"{Uri.UriSchemeFile}://{relativeTo}";
            }

            if (path.Length > 0 && path[0] == UnixPathRoot)
            {
                path = $"{Uri.UriSchemeFile}://{path}";
            }

            Uri relativeToUri = new Uri(relativeTo, UriKind.Absolute);
            Uri pathUri = new Uri(path, UriKind.Absolute);

            Uri relativeUri = relativeToUri.MakeRelativeUri(pathUri);

            string relativePath = relativeUri.ToString();

            // Decode/un-escape characters (e.g, "%20" back to " ")
            relativePath = Uri.UnescapeDataString(relativePath);

            // Convert to native path separators
            relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return relativePath;
        }
    }
}
