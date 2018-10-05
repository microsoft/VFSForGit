using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Service
{
    public class ProductUpgradeTimer
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
            Random random = new Random();
            TimeSpan startTime = TimeSpan.Zero;

            this.tracer.RelatedInfo("Starting auto upgrade checks.");
            this.timer = new Timer(
                this.TimerCallback,
                state: null,
                dueTime: startTime,
                period: TimeInterval);
        }

        public void Stop()
        {
            this.tracer.RelatedInfo("Stopping auto upgrade checks");
            this.timer.Dispose();
        }

        private void TimerCallback(object unusedState)
        {
            string errorMessage = null;

            InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer, string.Empty);
            if (prerunChecker.TryRunPreUpgradeChecks(out string _) && !this.TryDownloadUpgrade(out errorMessage))
            {
                this.tracer.RelatedError(errorMessage);
            }
        }

        private bool TryDownloadUpgrade(out string errorMessage)
        {
            using (ITracer activity = this.tracer.StartActivity("Checking for product upgrades.", EventLevel.Informational))
            {
                ProductUpgrader productUpgrader = new ProductUpgrader(ProcessHelper.GetCurrentProcessVersion(), this.tracer);
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
