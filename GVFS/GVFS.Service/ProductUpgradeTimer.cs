using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Service
{
    public class ProductUpgradeTimer : IDisposable
    {
        private static readonly TimeSpan TimeInterval = TimeSpan.FromDays(1);
        private JsonTracer tracer;
        private Timer timer;

        public ProductUpgradeTimer(JsonTracer tracer)
        {
            this.tracer = tracer;
        }

        public void Start()
        {
            if (!GVFSEnlistment.IsUnattended(this.tracer))
            {
                TimeSpan startTime = TimeSpan.Zero;

                this.tracer.RelatedInfo("Starting auto upgrade checks.");
                this.timer = new Timer(
                    this.TimerCallback,
                    state: null,
                    dueTime: startTime,
                    period: TimeInterval);
            }
            else
            {
                this.tracer.RelatedInfo("No upgrade checks scheduled, GVFS is running in unattended mode.");
            }
        }

        public void Stop()
        {
            this.tracer.RelatedInfo("Stopping auto upgrade checks");
            this.Dispose();
        }

        public void Dispose()
        {
            if (this.timer != null)
            {
                this.timer.Dispose();
                this.timer = null;
            }
        }

        private void TimerCallback(object unusedState)
        {
            string errorMessage = null;

            InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer, string.Empty);
            ProductUpgrader productUpgrader = new ProductUpgrader(ProcessHelper.GetCurrentProcessVersion(), this.tracer);
            if (prerunChecker.TryRunPreUpgradeChecks(out string _) && this.TryDownloadUpgrade(productUpgrader, out errorMessage))
            {
                return;
            }

            productUpgrader.CleanupDownloadDirectory();

            if (errorMessage != null)
            {
                this.tracer.RelatedError(errorMessage);
            }
        }

        private bool TryDownloadUpgrade(ProductUpgrader productUpgrader, out string errorMessage)
        {
            using (ITracer activity = this.tracer.StartActivity("Checking for product upgrades.", EventLevel.Informational))
            {
                Version newerVersion = null;
                string detailedError = null;
                if (!productUpgrader.TryGetNewerVersion(out newerVersion, out detailedError))
                {
                    errorMessage = "Could not fetch new version info. " + detailedError;
                    return false;
                }

                if (newerVersion == null)
                {
                    // Already up-to-date
                    // Make sure there a no asset installers remaining in the Downloads directory. This can happen if user
                    // upgraded by manually downloading and running asset installers.
                    productUpgrader.CleanupDownloadDirectory();
                    errorMessage = null;
                    return true;
                }

                if (productUpgrader.TryDownloadNewestVersion(out detailedError))
                {
                    errorMessage = null;
                    return true;
                }
                else
                {
                    errorMessage = "Could not download product upgrade. " + detailedError;
                    return false;
                }
            }
        }
    }
}
