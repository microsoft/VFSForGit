using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Platform.Mac
{
    public class MacProductUpgraderPlatformStrategy : ProductUpgraderPlatformStrategy
    {
        public MacProductUpgraderPlatformStrategy(PhysicalFileSystem fileSystem, ITracer tracer)
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
            string toolsDirectoryPath = ProductUpgraderInfo.GetUpgradeApplicationDirectory();

            Exception deleteDirectoryException;
            if (this.FileSystem.DirectoryExists(toolsDirectoryPath) &&
                !this.FileSystem.TryDeleteDirectory(toolsDirectoryPath, out deleteDirectoryException))
            {
                error = $"Failed to delete {toolsDirectoryPath} - {deleteDirectoryException.Message}";

                this.TraceException(deleteDirectoryException, nameof(this.TryPrepareApplicationDirectory), $"Error deleting {toolsDirectoryPath}.");
                return false;
            }

            this.FileSystem.CreateDirectory(toolsDirectoryPath);

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
