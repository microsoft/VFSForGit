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

        /// <summary>
        /// This is the name of the directory that the Upgrader Application is copied to
        /// when running upgrade. It is called "Tools", as this is what the directory was
        /// originally named, but has since been renamed in code to be more descriptive.
        /// </summary>
        public const string ApplicationDirectory = "Tools";
        public const string HighestAvailableVersionFileName = "HighestAvailableVersion";

        public static bool IsLocalUpgradeAvailable(ITracer tracer, string highestAvailableVersionDirectory)
        {
            try
            {
                return File.Exists(GetHighestAvailableVersionFilePath(highestAvailableVersionDirectory));
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

        private static string GetHighestAvailableVersionFilePath(string highestAvailableVersionDirectory)
        {
            return Path.Combine(highestAvailableVersionDirectory, HighestAvailableVersionFileName);
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
