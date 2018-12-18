using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public partial class ProductUpgrader
    {
        public static IProductUpgrader CreateUpgrader(ITracer tracer, out string error)
        {
            IProductUpgrader upgrader;
            bool isEnabled;
            bool isConfigured;
            error = string.Empty;

            upgrader = GitHubUpgrader.Create(tracer, out isEnabled, out isConfigured);
            if (upgrader != null)
            {
                return upgrader;
            }

            if (isEnabled && !isConfigured)
            {
                // Upgrader is enabled in LocalGVFSConfig. But one or more of the upgrade 
                // config settings are either missing or set incorreclty.
                return null;
            }

            if (tracer != null)
            {
                tracer.RelatedError($"{nameof(CreateUpgrader)}: Could not create upgrader. {error}");
            }

            error = GVFSConstants.UpgradeVerbMessages.InvalidRingConsoleAlert + Environment.NewLine + Environment.NewLine + "Error: " + error;
            return null;
        }
    }
}
