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
            TimeSpan variation = TimeSpan.FromMinutes(random.Next(0, 60));

            this.tracer.RelatedInfo("Starting auto upgrade checks");
            this.timer = new Timer(
                this.TimerCallback,
                null,
                TimeSpan.Zero.Add(variation),
                TimeInterval.Add(variation));
        }

        public void Stop()
        {
            this.tracer.RelatedInfo("Stopping auto upgrade checks");
            this.timer.Dispose();
        }

        private void TimerCallback(object unusedState)
        {
            string errorMessage = null;

            if (!this.TryDownloadUpgrade(out errorMessage))
            {
                this.tracer.RelatedError(errorMessage);
            }
        }

        private bool TryDownloadUpgrade(out string errorMessage)
        {
            this.tracer.RelatedInfo("Checking for product upgrades.");

            InstallerPreRunChecker prerunChecker = new InstallerPreRunChecker(this.tracer);
            if (!prerunChecker.TryRunPreUpgradeChecks(gitVersion: null, error: out errorMessage))
            {
                return false;
            }

            ProductUpgrader productUpgrader = new ProductUpgrader(ProcessHelper.GetCurrentProcessVersion(), this.tracer);
            Version newerVersion = null;
            string detailedError = null;
            if (!productUpgrader.TryGetNewerVersion(out newerVersion, out detailedError))
            {
                errorMessage = "Could not fetch new version info. " + detailedError;
                return false;
            }

            if (newerVersion != null && productUpgrader.TryDownloadNewestVersion(out detailedError))
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
