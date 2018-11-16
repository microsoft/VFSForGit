using GVFS.Common.Git;
using System;
using System.Threading;

namespace GVFS.Common.Cleanup
{
    public class GitCleanupScheduler : IDisposable
    {
        private readonly TimeSpan prefetchPeriod = TimeSpan.FromMinutes(15);
        private Timer prefetchStepTimer;
        private GVFSContext context;
        private GitObjects gitObjects;
        private GitCleanupQueue queue;

        public GitCleanupScheduler(GVFSContext context, GitObjects gitObjects, GitCleanupQueue queue)
        {
            this.context = context;
            this.gitObjects = gitObjects;
            this.queue = queue;

            this.ScheduleRecurringCleanupSteps();
        }

        public void Dispose()
        {
            this.prefetchStepTimer?.Dispose();
            this.prefetchStepTimer = null;
        }

        public void ScheduleRecurringCleanupSteps()
        {
            this.prefetchStepTimer = new Timer(
                (state) => this.queue.Enqueue(new PrefetchStep(this.context, this.gitObjects)),
                state: null,
                dueTime: this.prefetchPeriod,
                period: this.prefetchPeriod);
        }
    }
}
