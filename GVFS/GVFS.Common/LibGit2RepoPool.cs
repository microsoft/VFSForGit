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
        private const int TryTakeTimeoutMilliseconds = 10;

        private readonly BlockingCollection<LibGit2Repo> pool;
        private readonly Func<LibGit2Repo> createRepo;
        private readonly ITracer tracer;

        private readonly Timer repoDisposalTimer;
        private readonly TimeSpan repoDisposalDueTime = TimeSpan.FromMinutes(15);
        private readonly TimeSpan repoDisposalPeriod = TimeSpan.FromMinutes(1);
        private int numExternalRepos;

        public LibGit2RepoPool(
            ITracer tracer,
                Func<LibGit2Repo> createRepo,
                TimeSpan? repoDisposalDueTime = null,
                TimeSpan? repoDisposalPeriod = null)
        {
            this.createRepo = createRepo;
            this.tracer = tracer;
            this.pool = new BlockingCollection<LibGit2Repo>();

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

        public int NumActiveRepos => this.pool.Count + this.numExternalRepos;

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
            this.ResetRepoDisposalTimer();

            Interlocked.Increment(ref this.numExternalRepos);

            if (this.pool.TryTake(out LibGit2Repo repo, TryTakeTimeoutMilliseconds))
            {
                return repo;
            }

            return this.createRepo();
        }

        private void ReturnToPool(LibGit2Repo repo)
        {
            if (repo != null)
            {
                this.ResetRepoDisposalTimer();

                Interlocked.Decrement(ref this.numExternalRepos);

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
            if (this.pool.TryTake(out LibGit2Repo repo, TryTakeTimeoutMilliseconds))
            {
                repo.Dispose();
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
