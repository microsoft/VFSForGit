using GVFS.Common.Git;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Common.Maintenance
{
    public class GitMaintenanceScheduler : IDisposable
    {
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
            this.queue.Enqueue(step);
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

        public void ScheduleRecurringSteps()
        {
            if (this.context.Unattended)
            {
                return;
            }

            if (this.gitObjects.IsUsingCacheServer())
            {
                TimeSpan prefetchPeriod = TimeSpan.FromMinutes(15);
                this.stepTimers.Add(new Timer(
                    (state) => this.queue.Enqueue(new PrefetchStep(this.context, this.gitObjects, requireCacheLock: true)),
                    state: null,
                    dueTime: prefetchPeriod,
                    period: prefetchPeriod));
            }

            // TODO: Schedule more mainenance steps here.
        }
    }
}
