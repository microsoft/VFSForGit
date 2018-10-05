using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgrader
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";

        private const string RootDirectory = UpgradeDirectoryName;
        private const string GVFSInstallerFileNamePrefix = "SetupGVFS";
        private const string VFSForGitInstallerFileNamePrefix = "VFSForGit";

        public static bool IsLocalUpgradeAvailable()
        {
            string downloadDirectory = GetAssetDownloadsPath();
            if (Directory.Exists(downloadDirectory))
            {
                // This method is used Only by Git hooks. Git hooks does not have access
                // to GVFSPlatform to read platform specific file extensions. That is the
                // reason possible installer file extensions are defined here.
                HashSet<string> extensions = new HashSet<string>() { "EXE", "DMG", "RPM", "DEB" };
                HashSet<string> installerNames = new HashSet<string>()
                {
                    GVFSInstallerFileNamePrefix.ToUpperInvariant(),
                    VFSForGitInstallerFileNamePrefix.ToUpperInvariant()
                };

                foreach (string file in Directory.EnumerateFiles(downloadDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    string[] components = Path.GetFileName(file).ToUpperInvariant().Split('.');
                    int length = components.Length;
                    if (length >= 2 && installerNames.Contains(components[0]) && extensions.Contains(components[length - 1]))
                    {
                        return true;
                    }
                }
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
