using System;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    public static class Paths
    {
        public static string GetGVFSEnlistmentRoot(string directory)
        {
            return GetRoot(directory, GVFSConstants.DotGVFS.Root);
        }

        public static string GetGitEnlistmentRoot(string directory)
        {
            return GetRoot(directory, GVFSConstants.DotGit.Root);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return "GVFS_" + enlistmentRoot.ToUpper().Replace(':', '_');
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

        private static string GetRoot(string startingDirectory, string rootName)
        {
            startingDirectory = startingDirectory.TrimEnd(GVFSConstants.PathSeparator);
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
    }
}
