using GVFS.Common.Git;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Common.Maintenance
{
    public class GitMaintenanceScheduler : IDisposable
    {
        private readonly TimeSpan looseObjectsDueTime = TimeSpan.FromMinutes(5);
        private readonly TimeSpan looseObjectsPeriod = TimeSpan.FromHours(6);

        private readonly TimeSpan packfileDueTime = TimeSpan.FromMinutes(30);
        private readonly TimeSpan packfilePeriod = TimeSpan.FromHours(12);

        private readonly TimeSpan prefetchPeriod = TimeSpan.FromMinutes(15);

        private List<Timer> stepTimers;
        private GVFSContext context;
        private GitObjects gitObjects;
        private GitMaintenanceQueue queue;

        public GitMaintenanceScheduler(GVFSContext context, GitObjects gitObjects)
        {
            this.context = context;
            this.gitObjects = gitObjects;
            this.stepTimers = new List<Timer>();
            this.queue = new GitMaintenanceQueue(context);

            this.ScheduleRecurringSteps();
        }

        public void EnqueueOneTimeStep(GitMaintenanceStep step)
        {
            this.queue.TryEnqueue(step);
        }

        public void Dispose()
        {
            this.queue.Stop();

            foreach (Timer timer in this.stepTimers)
            {
                timer?.Dispose();
            }

            this.stepTimers = null;
        }

        private void ScheduleRecurringSteps()
        {
            if (this.context.Unattended)
            {
                return;
            }

            bool usingCacheServer = this.gitObjects.IsUsingCacheServer();

            if (usingCacheServer)
            {
                TimeSpan prefetchPeriod = TimeSpan.FromMinutes(15);
                this.stepTimers.Add(new Timer(
                    (state) => this.queue.TryEnqueue(new PrefetchStep(this.context, this.gitObjects)),
                    state: null,
                    dueTime: this.prefetchPeriod,
                    period: this.prefetchPeriod));
            }

            this.stepTimers.Add(new Timer(
                (state) => this.queue.TryEnqueue(new LooseObjectsStep(this.context)),
                state: null,
                dueTime: this.looseObjectsDueTime,
                period: this.looseObjectsPeriod));

            // When packfile-maintenance recovery removes a corrupt prefetch pack (and the later prefetch
            // packs that depend on it), it needs a prefetch to re-download them and rebuild the
            // commit-graph. This is only meaningful when a cache server is in use; otherwise the objects
            // are restored on demand.
            Action requestPrefetch = usingCacheServer
                ? () => this.queue.TryEnqueue(new PrefetchStep(this.context, this.gitObjects))
                : (Action)null;

            this.stepTimers.Add(new Timer(
                (state) => this.queue.TryEnqueue(new PackfileMaintenanceStep(this.context, requestPrefetch: requestPrefetch)),
                state: null,
                dueTime: this.packfileDueTime,
                period: this.packfilePeriod));
        }
    }
}
