using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public class ProductUpgraderFactory
    {
        public static bool TryCreateUpgrader(out IProductUpgrader newUpgrader, ITracer tracer, out string error)
        {
            newUpgrader = GitHubUpgrader.Create(tracer, out bool isEnabled, out bool isConfigured, out error);

            if (newUpgrader != null)
            {
                return true;
            }

            if (isEnabled && !isConfigured)
            {
                // Upgrader is enabled in LocalGVFSConfig. But one or more of the upgrade
                // config settings are either missing or set incorrectly.
                return false;
            }

            tracer?.RelatedError($"{nameof(TryCreateUpgrader)}: Could not create upgrader. {error}");

            error = GVFSConstants.UpgradeVerbMessages.InvalidRingConsoleAlert + Environment.NewLine + Environment.NewLine + "Error: " + error;
            return false;
        }
    }
}
