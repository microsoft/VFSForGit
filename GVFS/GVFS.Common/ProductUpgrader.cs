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
            
            if ((upgrader = GitHubUpgrader.Create(tracer, out isEnabled, out isConfigured)) != null)
            {
                return upgrader;
            }

            if (isEnabled && !isConfigured)
            {
                // Configuration error
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
