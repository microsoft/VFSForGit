using System;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgrader
    {
        public const string UpgraderName = "GVFS.Upgrade";

        private const string RootDirectory = UpgraderName;
        private const string DownloadDirectory = "Downloads";
        private const string ToolsDirectory = "Tools";
        private const string LogDirectory = "Logs";
        private const string UpgraderToolName = "GVFS.Upgrader.exe";
        private static readonly string[] UpgraderToolAndLibs =
            {
                "GVFS.Upgrader.exe",
                "GVFS.Common.dll",
                "GVFS.Platform.Windows.dll",
                "Microsoft.Diagnostics.Tracing.EventSource.dll",
                "netstandard.dll",
                "Newtonsoft.Json.dll"
            };

        public static bool IsLocalUpgradeAvailable()
        {
            string downloadDirectory = GetAssetDownloadsPath();
            if (Directory.Exists(downloadDirectory))
            {
                return Directory.GetFiles(GetAssetDownloadsPath()).Length > 0;
            }

            return false;
        }

        public static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
        }

        public static string GetLogDirectoryName()
        {
            return LogDirectory;
        }

        public static string GetLogDirectoryPath()
        {
            return Path.Combine(Paths.GetServiceDataRoot(RootDirectory), LogDirectory);
        }

        private static bool TryCreateDirectory(string path, out string error)
        {
            error = null;

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException exception)
            {
                error = exception.ToString();
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                error = exception.ToString();
                return false;
            }

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
