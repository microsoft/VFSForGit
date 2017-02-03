using System;
using System.Threading;

namespace FastFetch.Jobs
{
    public abstract class Job
    {
        private int maxParallel;
        private Thread[] workers;

        public Job(int maxParallel)
        {
            this.maxParallel = maxParallel;
        }
        
        public bool HasFailures { get; protected set; }

        public void Start()
        {
            if (this.workers != null)
            {
                throw new InvalidOperationException("Cannot call start twice");
            }
            
            this.DoBeforeWork();

            this.workers = new Thread[this.maxParallel];
            for (int i = 0; i < this.workers.Length; ++i)
            {
                this.workers[i] = new Thread(this.DoWork);
                this.workers[i].Start();
            }
        }

        public void WaitForCompletion()
        {
            if (this.workers == null)
            {
                throw new InvalidOperationException("Cannot wait for completion before start is called");
            }

            foreach (Thread t in this.workers)
            {
                t.Join();
            }

            this.DoAfterWork();
            this.workers = null;
        }

        protected virtual void DoBeforeWork()
        {
        }

        protected abstract void DoWork();

        protected abstract void DoAfterWork();
    }
}
