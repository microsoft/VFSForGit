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
        private const int TryTakeWaitShort = 10;

        private readonly BlockingCollection<LibGit2Repo> pool;
        private readonly Func<LibGit2Repo> repoFactory;
        private readonly ITracer tracer;

        private readonly Timer repoDisposalTimer;
        private readonly TimeSpan repoDisposalDueTime = TimeSpan.FromMinutes(1);
        private readonly TimeSpan repoDisposalPeriod = TimeSpan.FromSeconds(15);
        private int numDisposedRepos;

        public LibGit2RepoPool(ITracer tracer, Func<LibGit2Repo> createRepo, int size)
        {
            if (size <= 0)
            {
                throw new ArgumentException("ProcessPool: size must be greater than 0");
            }

            this.repoFactory = createRepo;
            this.tracer = tracer;
            this.pool = new BlockingCollection<LibGit2Repo>();
            for (int i = 0; i < size; ++i)
            {
                this.pool.Add(createRepo());
            }

            this.repoDisposalTimer = new Timer(
                                             (state) => this.TryDropARepo(),
                                             state: null,
                                             dueTime: this.repoDisposalDueTime,
                                             period: this.repoDisposalPeriod);
        }

        public void Dispose()
        {
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
            this.ResetTimer();

            LibGit2Repo repo;
            if (this.pool.TryTake(out repo, TryTakeWaitShort))
            {
                return repo;
            }

            if (this.numDisposedRepos > 0)
            {
                if (Interlocked.Decrement(ref this.numDisposedRepos) >= 0)
                {
                    return this.repoFactory();
                }
                else
                {
                    Interlocked.Increment(ref this.numDisposedRepos);
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
                this.ResetTimer();

                if (this.pool.IsAddingCompleted ||
                    !this.pool.TryAdd(repo, TryAddTimeoutMilliseconds))
                {
                    // No more adding to the pool or trying to add to the pool failed
                    repo.Dispose();
                }
            }
        }

        private void ResetTimer()
        {
            this.repoDisposalTimer.Change(this.repoDisposalDueTime, this.repoDisposalPeriod);
        }

        private void TryDropARepo()
        {
            if (this.pool.TryTake(out LibGit2Repo repo, TryTakeWaitShort))
            {
                repo.Dispose();
                Interlocked.Increment(ref this.numDisposedRepos);
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
