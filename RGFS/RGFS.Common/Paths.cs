using System;
using System.IO;
using System.Linq;

namespace RGFS.Common
{
    public static class Paths
    {
        public static string GetRGFSEnlistmentRoot(string directory)
        {
            return GetRoot(directory, RGFSConstants.DotRGFS.Root);
        }

        public static string GetGitEnlistmentRoot(string directory)
        {
            return GetRoot(directory, RGFSConstants.DotGit.Root);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return "RGFS_" + enlistmentRoot.ToUpper().Replace(':', '_');
        }

        public static string GetServiceDataRoot(string serviceName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "RGFS",
                serviceName);
        }

        public static string GetServiceLogsPath(string serviceName)
        { 
            return Path.Combine(GetServiceDataRoot(serviceName), "Logs");
        }

        private static string GetRoot(string startingDirectory, string rootName)
        {
            startingDirectory = startingDirectory.TrimEnd(RGFSConstants.PathSeparator);
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
                    DirectoryInfo[] dotRGFSDirs = new DirectoryInfo[0];

                    try
                    {
                        dotRGFSDirs = dirInfo.GetDirectories(rootName);
                    }
                    catch (IOException)
                    {
                    }

                    if (dotRGFSDirs.Count() == 1)
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
