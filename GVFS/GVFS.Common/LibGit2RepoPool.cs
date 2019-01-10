using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common
{
    public class LibGit2RepoPool
    {
        private static readonly TimeSpan DefaultRepositoryDisposalPeriod = TimeSpan.FromMinutes(15);

        private readonly Func<LibGit2Repo> createRepo;
        private readonly ITracer tracer;
        private readonly object sharedRepoLock = new object();
        private readonly TimeSpan sharedRepositoryDisposalPeriod;
        private bool stopped;
        private LibGit2Repo sharedRepo;
        private Timer sharedRepoDisposalTimer;
        private int activeCallers;

        public LibGit2RepoPool(ITracer tracer, Func<LibGit2Repo> createRepo, int size = 1, TimeSpan? disposalPeriod = null)
        {
            if (size <= 0)
            {
                throw new ArgumentException("ProcessPool: size must be greater than 0");
            }

            this.tracer = tracer;
            this.createRepo = createRepo;

            this.sharedRepositoryDisposalPeriod = disposalPeriod ?? DefaultRepositoryDisposalPeriod;

            this.sharedRepoDisposalTimer = new Timer(
                (state) => this.DisposeSharedRepo(),
                state: null,
                dueTime: this.sharedRepositoryDisposalPeriod,
                period: this.sharedRepositoryDisposalPeriod);
        }

        public void Dispose()
        {
            this.stopped = true;

            lock (this.sharedRepoLock)
            {
                this.sharedRepo?.Dispose();
                this.sharedRepo = null;
            }
        }

        public bool TryInvoke<TResult>(Func<LibGit2Repo, TResult> function, out TResult result)
        {
            if (this.stopped)
            {
                result = default(TResult);
                return false;
            }

            try
            {
                Interlocked.Increment(ref this.activeCallers);
                LibGit2Repo repo = this.GetRepoFromPool();

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
                Interlocked.Decrement(ref this.activeCallers);
            }
        }

        private LibGit2Repo GetRepoFromPool()
        {
            this.sharedRepoDisposalTimer.Change(this.sharedRepositoryDisposalPeriod, this.sharedRepositoryDisposalPeriod);

            lock (this.sharedRepoLock)
            {
                if (this.stopped)
                {
                    return null;
                }

                if (this.sharedRepo == null)
                {
                    this.sharedRepo = this.createRepo();
                }

                return this.sharedRepo;
            }
        }

        private void DisposeSharedRepo()
        {
            lock (this.sharedRepoLock)
            {
                if (this.stopped || this.activeCallers > 0)
                {
                    return;
                }

                LibGit2Repo repo = this.sharedRepo;
                this.sharedRepo = null;
                repo?.Dispose();
            }
        }
    }
}
