using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
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

        public void RecordHighestAvailableVersion(Version highestAvailableVersion)
        {
            string highestAvailableVersionFile = GetHighestAvailableVersionFilePath();

            if (highestAvailableVersion == null)
            {
                if (this.fileSystem.FileExists(highestAvailableVersionFile))
                {
                    this.fileSystem.DeleteFile(highestAvailableVersionFile);
                }
            }
            else
            {
                this.fileSystem.WriteAllText(highestAvailableVersionFile, highestAvailableVersion.ToString());
            }
        }
    }
}
