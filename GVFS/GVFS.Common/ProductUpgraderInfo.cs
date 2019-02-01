using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class ProductUpgraderInfo
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";
        public const string HighestAvailableVersionFileName = "HighestAvailableVersion";

        protected const string RootDirectory = UpgradeDirectoryName;

        public static bool IsLocalUpgradeAvailable(ITracer tracer)
        {
            try
            {
                return File.Exists(GetHighestAvailableVersionFilePath());
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is NotSupportedException)
            {
                if (tracer != null)
                {
                    tracer.RelatedError(
                        CreateEventMetadata(ex),
                        "Exception encountered when determining if an upgrade is available.");
                }
            }

            return false;
        }

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

        public static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
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

        public static string GetHighestAvailableVersionFilePath()
        {
            return Path.Combine(GetUpgradesDirectoryPath(), HighestAvailableVersionFileName);
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

        private static EventMetadata CreateEventMetadata(Exception e)
        {
            EventMetadata metadata = new EventMetadata();
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }
    }
}
