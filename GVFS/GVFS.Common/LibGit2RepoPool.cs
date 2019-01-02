using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GVFS.Common
{
    public class LibGit2RepoPool
    {
        private const int TryAddTimeoutMilliseconds = 10;
        private const int TryTakeWaitForever = Timeout.Infinite;
        private const int TryTakeNoWait = 0;

        private readonly BlockingCollection<LibGit2Repo> pool;
        private readonly Func<LibGit2Repo> createRepo;
        private readonly ITracer tracer;

        private readonly Timer repoDisposalTimer;
        private readonly TimeSpan repoDisposalDueTime = TimeSpan.FromMinutes(15);
        private readonly TimeSpan repoDisposalPeriod = TimeSpan.FromMinutes(1);
        private readonly int maxRepoAllocations;
        private int numAvailableRepoAllocations;
        private int numWaitingThreads;
        private object numWaitingThreadsLock = new object();

        public LibGit2RepoPool(
            ITracer tracer,
                Func<LibGit2Repo> createRepo,
                int size,
                TimeSpan? repoDisposalDueTime = null,
                TimeSpan? repoDisposalPeriod = null)
        {
            if (size <= 0)
            {
                throw new ArgumentException("ProcessPool: size must be greater than 0");
            }

            this.maxRepoAllocations = size;
            this.createRepo = createRepo;
            this.tracer = tracer;
            this.pool = new BlockingCollection<LibGit2Repo>();
            for (int i = 0; i < size; ++i)
            {
                this.pool.Add(createRepo());
            }

            if (repoDisposalDueTime.HasValue)
            {
                this.repoDisposalDueTime = repoDisposalDueTime.Value;
            }

            if (repoDisposalPeriod.HasValue)
            {
                this.repoDisposalPeriod = repoDisposalPeriod.Value;
            }

            this.repoDisposalTimer = new Timer(
                                             (state) => this.TryDropARepo(),
                                             state: null,
                                             dueTime: this.repoDisposalDueTime,
                                             period: this.repoDisposalPeriod);
        }

        public int NumActiveRepos => this.pool.Count + (this.maxRepoAllocations - this.numAvailableRepoAllocations);

        public void Dispose()
        {
            this.repoDisposalTimer.Dispose();
            this.pool.CompleteAdding();
            this.CleanUpPool();
        }

        public bool TryInvoke<TResult>(Func<LibGit2Repo, TResult> function, out TResult result)
        {
            LibGit2Repo repo = null;

            try
            {
                repo = this.GetRepoFromPool();
                if (repo != null)
                {
                    result = function(repo);
                    return true;
                }

                result = default(TResult);
                return false;
            }
            catch (Exception e)
            {
                this.tracer.RelatedWarning("Exception while invoking libgit2: " + e.ToString(), Keywords.Telemetry);
                throw;
            }
            finally
            {
                if (repo != null)
                {
                    this.ReturnToPool(repo);
                }
            }
        }

        private LibGit2Repo GetRepoFromPool()
        {
            try
            {
                lock (this.numWaitingThreadsLock)
                {
                    this.numWaitingThreads++;
                }

                return this.WaitForRepoFromPool();
            }
            finally
            {
                lock (this.numWaitingThreadsLock)
                {
                    this.numWaitingThreads--;
                }
            }
        }

        private LibGit2Repo WaitForRepoFromPool()
        {
            this.ResetRepoDisposalTimer();

            LibGit2Repo repo;

            if (this.pool.TryTake(out repo, TryTakeNoWait))
            {
                return repo;
            }

            if (this.numAvailableRepoAllocations > 0)
            {
                if (Interlocked.Decrement(ref this.numAvailableRepoAllocations) >= 0)
                {
                    return this.createRepo();
                }
                else
                {
                    Interlocked.Increment(ref this.numAvailableRepoAllocations);
                }
            }

            if (this.pool.TryTake(out repo, TryTakeWaitForever))
            {
                return repo;
            }

            // This should only happen when the pool is shutting down
            return null;
        }

        private void ReturnToPool(LibGit2Repo repo)
        {
            if (repo != null)
            {
                this.ResetRepoDisposalTimer();

                if (this.pool.IsAddingCompleted ||
                    !this.pool.TryAdd(repo, TryAddTimeoutMilliseconds))
                {
                    // No more adding to the pool or trying to add to the pool failed
                    repo.Dispose();
                }
            }
        }

        private void ResetRepoDisposalTimer()
        {
            this.repoDisposalTimer.Change(this.repoDisposalDueTime, this.repoDisposalPeriod);
        }

        private void TryDropARepo()
        {
            if (this.pool.TryTake(out LibGit2Repo repo, TryTakeNoWait))
            {
                lock (this.numWaitingThreadsLock)
                {
                    if (this.numWaitingThreads == 0)
                    {
                        repo.Dispose();
                        Interlocked.Increment(ref this.numAvailableRepoAllocations);
                    }
                    else
                    {
                        this.ReturnToPool(repo);
                    }
                }
            }
        }

        private void CleanUpPool()
        {
            LibGit2Repo repo;
            while (this.pool.TryTake(out repo))
            {
                repo.Dispose();
            }
        }
    }
}
