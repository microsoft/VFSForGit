using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgraderInfo
    {
        private ITracer tracer;
        private PhysicalFileSystem fileSystem;

        public ProductUpgraderInfo(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            this.tracer = tracer;
            this.fileSystem = fileSystem;
        }

        public static string CurrentGVFSVersion()
        {
            return ProcessHelper.GetCurrentProcessVersion();
        }

        public static string GetUpgradeProtectedDataDirectory()
        {
            return GVFSPlatform.Instance.GetUpgradeProtectedDataDirectory();
        }

        public static string GetUpgradeApplicationDirectory()
        {
            return Path.Combine(
                GetUpgradeProtectedDataDirectory(),
                ProductUpgraderInfo.ApplicationDirectory);
        }

        public static string GetParentLogDirectoryPath()
        {
            return GVFSPlatform.Instance.GetUpgradeLogDirectoryParentDirectory();
        }

        public static string GetLogDirectoryPath()
        {
            return Path.Combine(
                GVFSPlatform.Instance.GetUpgradeLogDirectoryParentDirectory(),
                ProductUpgraderInfo.LogDirectory);
        }

        public static string GetAssetDownloadsPath()
        {
            return Path.Combine(
                GVFSPlatform.Instance.GetUpgradeProtectedDataDirectory(),
                ProductUpgraderInfo.DownloadDirectory);
        }

        public static string GetHighestAvailableVersionDirectory()
        {
            return GVFSPlatform.Instance.GetUpgradeHighestAvailableVersionDirectory();
        }

        public void DeleteAllInstallerDownloads()
        {
            try
            {
                this.fileSystem.DeleteDirectory(GetAssetDownloadsPath());
            }
            catch (Exception ex)
            {
                if (this.tracer != null)
                {
                    this.tracer.RelatedError($"{nameof(this.DeleteAllInstallerDownloads)}: Could not remove directory: {ProductUpgraderInfo.GetAssetDownloadsPath()}.{ex.ToString()}");
                }
            }
        }

        public void RecordHighestAvailableVersion(Version highestAvailableVersion)
        {
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath(GetHighestAvailableVersionDirectory());

            if (highestAvailableVersion == null)
            {
                if (this.fileSystem.FileExists(highestAvailableVersionFile))
                {
                    this.fileSystem.DeleteFile(highestAvailableVersionFile);

                    if (this.tracer != null)
                    {
                        this.tracer.RelatedInfo($"{nameof(this.RecordHighestAvailableVersion)}: Deleted upgrade reminder marker file");
                    }
                }
            }
            else
            {
                this.fileSystem.WriteAllText(highestAvailableVersionFile, highestAvailableVersion.ToString());

                if (this.tracer != null)
                {
                    this.tracer.RelatedInfo($"{nameof(this.RecordHighestAvailableVersion)}: Created upgrade reminder marker file");
                }
            }
        }
    }
}
