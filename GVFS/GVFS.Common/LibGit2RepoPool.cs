using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace GVFS.Common
{
    public class LibGit2RepoPool
    {
        private const int TryAddTimeoutMilliseconds = 10;
        private const int TryTakeTimeoutMilliseconds = -1;
        
        private readonly BlockingCollection<LibGit2Repo> pool;
        private readonly ITracer tracer;

        public LibGit2RepoPool(ITracer tracer, Func<LibGit2Repo> createRepo, int size)
        {
            if (size <= 0)
            {
                throw new ArgumentException("ProcessPool: size must be greater than 0");
            }

            this.tracer = tracer;
            this.pool = new BlockingCollection<LibGit2Repo>();
            for (int i = 0; i < size; ++i)
            {
                this.pool.Add(createRepo());
            }
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
                this.tracer.RelatedError("Exception while invoking libgit2: " + e.ToString());
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
            LibGit2Repo repo;
            if (this.pool.TryTake(out repo, TryTakeTimeoutMilliseconds))
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
                if (this.pool.IsAddingCompleted ||
                    !this.pool.TryAdd(repo, TryAddTimeoutMilliseconds))
                {
                    // No more adding to the pool or trying to add to the pool failed
                    repo.Dispose();
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
