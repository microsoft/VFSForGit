using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    public class ProcessPool<TProcess> : IDisposable where TProcess : GitCatFileProcess
    {
        private const int TryAddTimeoutMilliseconds = 10;
        private const int TryTakeTimeoutMilliseconds = 10;
        private const int IdleSecondsBeforeCleanup = 10;

        // To help the idle processes to get cleaned up close to when the process passes the IdleSecondsBeforeCleanup
        // we set the timer to pop at half the IdleSecondsBeforeCleanup value
        private const int CleanupTimerPeriodMilliseconds = IdleSecondsBeforeCleanup * 1000 / 2;

        private readonly Func<TProcess> createProcess;
        private readonly BlockingCollection<RunningProcess> pool;
        private readonly ITracer tracer;
        private readonly Timer cleanupTimer;

        public ProcessPool(ITracer tracer, Func<TProcess> createProcess, int size)
        {
            Debug.Assert(size > 0, "ProcessPool: size must be greater than 0");

            this.tracer = tracer;
            this.createProcess = createProcess;
            this.pool = new BlockingCollection<RunningProcess>(size);
            this.cleanupTimer = new Timer(x => this.CleanUpPool(shutdownAllProcesses: false), null, 0, CleanupTimerPeriodMilliseconds);
        }

        public void Dispose()
        {
            this.cleanupTimer.Change(Timeout.Infinite, Timeout.Infinite);
            this.cleanupTimer.Dispose();
            this.pool.CompleteAdding();
            this.CleanUpPool(shutdownAllProcesses: true);
        }

        public void Invoke(Action<TProcess> function)
        {
            this.Invoke(process => { function(process); return false; });
        }

        public TResult Invoke<TResult>(Func<TProcess, TResult> function)
        {
            TProcess process = null;
            bool returnToPool = true;

            try
            {
                process = this.GetRunningProcessFromPool();
                TResult result = function(process);

                // Retry once if the process crashed while we were running it
                if (!process.IsRunning())
                {
                    process = this.GetRunningProcessFromPool();
                    result = function(process);
                }

                return result;
            }
            catch
            {
                returnToPool = false;
                throw;
            }
            finally
            {
                if (returnToPool)
                {
                    this.ReturnToPool(process);
                }
                else
                {
                    process.Kill();
                }
            }
        }

        private TProcess GetRunningProcessFromPool()
        {
            RunningProcess poolProcess;
            if (this.pool.TryTake(out poolProcess, TryTakeTimeoutMilliseconds))
            {
                return poolProcess.Process;
            }
            else
            {
                return this.createProcess();
            }
        }

        private void ReturnToPool(TProcess process)
        {
            if (process != null && process.IsRunning())
            {
                if (this.pool.IsAddingCompleted ||
                    !this.pool.TryAdd(new RunningProcess(process), TryAddTimeoutMilliseconds))
                {
                    // No more adding to the pool or trying to add to the pool failed
                    process.Kill();
                }
            }
        }

        private void CleanUpPool(bool shutdownAllProcesses)
        {
            int numberInPool = this.pool.Count;
            for (int i = 0; i < numberInPool; i++)
            {
                RunningProcess poolProcess;
                if (this.pool.TryTake(out poolProcess))
                {
                    if (shutdownAllProcesses || this.pool.IsAddingCompleted)
                    {
                        poolProcess.Dispose();
                    }
                    else if (poolProcess.Process.IsRunning() &&
                        poolProcess.LastUsed.AddSeconds(IdleSecondsBeforeCleanup) > DateTime.Now)
                    {
                        this.pool.TryAdd(poolProcess, TryAddTimeoutMilliseconds);
                    }
                    else
                    {
                        // Process is either not running or has been idle too long
                        poolProcess.Dispose();
                    }
                }
            }
        }

        private class RunningProcess : IDisposable
        {
            public RunningProcess(TProcess process)
            {
                this.Process = process;
                this.LastUsed = DateTime.Now;
            }

            public TProcess Process { get; private set; }
            public DateTime LastUsed { get; }

            public void Dispose()
            {
                if (this.Process != null)
                {
                    this.Process.Dispose();
                    this.Process = null;
                }
            }
        }
    }
}
