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

        public GitMaintenanceScheduler(GVFSContext context, GitObjects gitObjects)
        {
            this.context = context;
            this.gitObjects = gitObjects;
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
