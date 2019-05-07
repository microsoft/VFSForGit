using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgraderInfo
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "UpgraderLogs";
        public const string DownloadDirectory = "Downloads";
        public const string HighestAvailableVersionFileName = "HighestAvailableVersion";

        public static bool IsLocalUpgradeAvailable(ITracer tracer, string gvfsDataRoot)
        {
            try
            {
                string upgradesDirectory = Path.Combine(gvfsDataRoot, UpgradeDirectoryName);

                return File.Exists(GetHighestAvailableVersionFilePath(upgradesDirectory));
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

        private static string GetHighestAvailableVersionFilePath(string upgradesDirectory)
        {
            return Path.Combine(upgradesDirectory, HighestAvailableVersionFileName);
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
