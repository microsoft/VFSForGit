using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Platform.Linux
{
    public class LinuxProductUpgraderPlatformStrategy : ProductUpgraderPlatformStrategy
    {
        public LinuxProductUpgraderPlatformStrategy(PhysicalFileSystem fileSystem, ITracer tracer)
        : base(fileSystem, tracer)
        {
        }

        public override bool TryPrepareLogDirectory(out string error)
        {
            error = null;
            return true;
        }

        public override bool TryPrepareApplicationDirectory(out string error)
        {
            string upgradeApplicationDirectory = ProductUpgraderInfo.GetUpgradeApplicationDirectory();

            Exception deleteDirectoryException;
            if (this.FileSystem.DirectoryExists(upgradeApplicationDirectory) &&
                !this.FileSystem.TryDeleteDirectory(upgradeApplicationDirectory, out deleteDirectoryException))
            {
                error = $"Failed to delete {upgradeApplicationDirectory} - {deleteDirectoryException.Message}";

                this.TraceException(deleteDirectoryException, nameof(this.TryPrepareApplicationDirectory), $"Error deleting {upgradeApplicationDirectory}.");
                return false;
            }

            this.FileSystem.CreateDirectory(upgradeApplicationDirectory);

            error = null;
            return true;
        }

        public override bool TryPrepareDownloadDirectory(out string error)
        {
            string directory = ProductUpgraderInfo.GetAssetDownloadsPath();

            Exception deleteDirectoryException;
            if (this.FileSystem.DirectoryExists(directory) &&
                !this.FileSystem.TryDeleteDirectory(directory, out deleteDirectoryException))
            {
                error = $"Failed to delete {directory} - {deleteDirectoryException.Message}";

                this.TraceException(deleteDirectoryException, nameof(this.TryPrepareDownloadDirectory), $"Error deleting {directory}.");
                return false;
            }

            this.FileSystem.CreateDirectory(directory);

            error = null;
            return true;
        }
    }
}
