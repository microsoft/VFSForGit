using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgraderInfo
    {
        public static void RecordHighestAvailableVersion(Version highestAvailableVersion)
        {
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath();

            if (highestAvailableVersion == null)
            {
                if (File.Exists(highestAvailableVersionFile))
                {
                    File.Delete(highestAvailableVersionFile);
                }
            }
            else
            {
                File.WriteAllText(highestAvailableVersionFile, highestAvailableVersion.ToString());
            }
        }

        public static string CurrentGVFSVersion()
        {
            return ProcessHelper.GetCurrentProcessVersion();
        }

        public static string GetLogDirectoryPath()
        {
            return Path.Combine(Paths.GetServiceDataRoot(RootDirectory), LogDirectory);
        }

        public static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                Paths.GetServiceDataRoot(RootDirectory),
                DownloadDirectory);
        }

        /// <summary>
        /// Deletes any previously downloaded installers in the Upgrader Download directory.
        /// This can include old installers which were downloaded, but user never installed
        /// using gvfs upgrade and GVFS is now up to date already.
        /// </summary>
        public static void DeleteAllInstallerDownloads(ITracer tracer = null)
        {
            try
            {
                RecursiveDelete(ProductUpgraderInfo.GetAssetDownloadsPath());
            }
            catch (Exception ex)
            {
                if (tracer != null)
                {
                    tracer.RelatedError($"{nameof(DeleteAllInstallerDownloads)}: Could not remove directory: {ProductUpgraderInfo.GetAssetDownloadsPath()}.{ex.ToString()}");
                }
            }
        }

        private static void RecursiveDelete(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            DirectoryInfo directory = new DirectoryInfo(path);

            foreach (FileInfo file in directory.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }

            foreach (DirectoryInfo subDirectory in directory.GetDirectories())
            {
                RecursiveDelete(subDirectory.FullName);
            }

            directory.Delete();
        }
    }
}
