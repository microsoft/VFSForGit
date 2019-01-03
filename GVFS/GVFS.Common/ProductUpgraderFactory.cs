using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public class ProductUpgraderFactory
    {
        public static bool TryCreateUpgrader(out IProductUpgrader newUpgrader, ITracer tracer, out string error)
        {
            IProductUpgrader upgrader;
            bool isEnabled;
            bool isConfigured;

            newUpgrader = null;
            upgrader = GitHubUpgrader.Create(tracer, out isEnabled, out isConfigured, out error);
            if (upgrader != null)
            {
                newUpgrader = upgrader;
                return true;
            }

            if (isEnabled && !isConfigured)
            {
                // Upgrader is enabled in LocalGVFSConfig. But one or more of the upgrade
                // config settings are either missing or set incorrectly.
                return false;
            }

            if (tracer != null)
            {
                tracer.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");
            }

            error = GVFSConstants.UpgradeVerbMessages.InvalidRingConsoleAlert + Environment.NewLine + Environment.NewLine + "Error: " + error;
            return false;
        }
    }
}
