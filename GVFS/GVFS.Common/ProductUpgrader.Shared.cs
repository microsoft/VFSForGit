using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgrader
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";

        private const string RootDirectory = UpgradeDirectoryName;
        private const string DownloadDirectory = "Downloads";
        private const string GVFSInstallerFileNamePrefix = "SetupGVFS";

        public static bool IsLocalUpgradeAvailable()
        {
            string downloadDirectory = GetAssetDownloadsPath();
            if (Directory.Exists(downloadDirectory))
            {
                string[] installers = Directory.GetFiles(
                    GetAssetDownloadsPath(), 
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

        private static bool TryCreateDirectory(string path, out Exception exception)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException e)
            {
                exception = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
                return false;
            }

            exception = null;
            return true;
        }

        private static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                DownloadDirectory);
        }
    }
}
