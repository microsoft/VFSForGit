using GVFS.Common.Git;
using System;
using System.Threading;

namespace GVFS.Common.Maintenance
{
    public class GitMaintenanceScheduler : IDisposable
    {
        private readonly TimeSpan prefetchPeriod = TimeSpan.FromMinutes(15);
        private Timer prefetchStepTimer;
        private GVFSContext context;
        private GitObjects gitObjects;
        private GitMaintenanceQueue queue;

        public GitMaintenanceScheduler(GVFSContext context, GitObjects gitObjects, GitMaintenanceQueue queue)
        {
            this.context = context;
            this.gitObjects = gitObjects;
            this.queue = queue;

            this.ScheduleRecurringSteps();
        }

        public void Dispose()
        {
            this.prefetchStepTimer?.Dispose();
            this.prefetchStepTimer = null;
        }

        public void ScheduleRecurringSteps()
        {
            if (!this.context.Unattended && this.gitObjects.IsUsingCacheServer())
            {
                this.prefetchStepTimer = new Timer(
                (state) => this.queue.Enqueue(new PrefetchStep(this.context, this.gitObjects, requireCacheLock: true)),
                state: null,
                dueTime: this.prefetchPeriod,
                period: this.prefetchPeriod);
            }
        }
    }
}
