using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgraderInfo
    {
        public const string UpgradeDirectoryName = "GVFS.Upgrade";
        public const string LogDirectory = "Logs";
        public const string DownloadDirectory = "Downloads";
        public const string HighestAvailableVersionFileName = "HighestAvailableVersion";

        private const string RootDirectory = UpgradeDirectoryName;

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

        public static string GetHighestAvailableVersionFilePath()
        {
            return Path.Combine(GetUpgradesDirectoryPath(), HighestAvailableVersionFileName);
        }

        public static string GetUpgradesDirectoryPath()
        {
            return Paths.GetServiceDataRoot(RootDirectory);
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
