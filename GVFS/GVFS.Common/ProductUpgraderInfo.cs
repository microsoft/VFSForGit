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

        public static string GetUpgradeApplicationDirectory()
        {
            return GVFSPlatform.Instance.ProductUpgraderInfoImpl.GetUpgradeApplicationDirectory();
        }

        public static string GetUpgradeProtectedDataDirectory()
        {
            return GVFSPlatform.Instance.ProductUpgraderInfoImpl.GetUpgradeProtectedDirectory();
        }

        public static string GetUpgradeBasicDataDirectory()
        {
            return GVFSPlatform.Instance.ProductUpgraderInfoImpl.GetUpgradeNonProtectedDirectory();
        }

        public static string GetLogDirectoryPath()
        {
            return Path.Combine(GetUpgradeBasicDataDirectory(), LogDirectory);
        }

        public static string GetAssetDownloadsPath()
        {
            return GVFSPlatform.Instance.ProductUpgraderInfoImpl.GetAssetDownloadsPath();
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
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath(GetUpgradeBasicDataDirectory());

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
