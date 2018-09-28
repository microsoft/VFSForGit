using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgrader
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";

        private const string RootDirectory = UpgradeDirectoryName;
        private const string GVFSInstallerFileNamePrefix = "VFSGit";

        public static bool IsLocalUpgradeAvailable()
        {
            string downloadDirectory = GetAssetDownloadsPath();
            if (Directory.Exists(downloadDirectory))
            {
                string[] installers = Directory.GetFiles(
                    downloadDirectory, 
                    $"{GVFSInstallerFileNamePrefix}*.*", 
                    SearchOption.TopDirectoryOnly);
                return installers.Length > 0;
            }

            return false;
        }

        public static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
        }
        
        public static string GetLogDirectoryPath()
        {
            return Path.Combine(Paths.GetServiceDataRoot(RootDirectory), LogDirectory);
        }

        private static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                DownloadDirectory);
        }
    }
}
