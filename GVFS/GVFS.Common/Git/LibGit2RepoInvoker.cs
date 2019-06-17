using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common.Git
{
    public class LibGit2RepoInvoker : IDisposable
    {
        private readonly Func<LibGit2Repo> createRepo;
        private readonly ITracer tracer;
        private readonly object sharedRepoLock = new object();
        private volatile bool disposing;
        private volatile int activeCallers;
        private LibGit2Repo sharedRepo;

        public LibGit2RepoInvoker(ITracer tracer, Func<LibGit2Repo> createRepo)
        {
            this.tracer = tracer;
            this.createRepo = createRepo;

            this.InitializeSharedRepo();
        }

        public void Dispose()
        {
            this.disposing = true;

            lock (this.sharedRepoLock)
            {
                this.sharedRepo?.Dispose();
                this.sharedRepo = null;
            }
        }

        public bool TryInvoke<TResult>(Func<LibGit2Repo, TResult> function, out TResult result)
        {
            try
            {
                Interlocked.Increment(ref this.activeCallers);
                LibGit2Repo repo = this.GetSharedRepo();

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

        public void DisposeSharedRepo()
        {
            lock (this.sharedRepoLock)
            {
                if (this.disposing || this.activeCallers > 0)
                {
                    return;
                }

                this.sharedRepo?.Dispose();
                this.sharedRepo = null;
            }
        }

        public void InitializeSharedRepo()
        {
            // Run a test on the shared repo to ensure the object store
            // is loaded, as that is what takes a long time with many packs.
            // Using a potentially-real object id is important, as the empty
            // SHA will stop early instead of loading the object store.
            this.GetSharedRepo()?.ObjectExists("30380be3963a75e4a34e10726795d644659e1129");
        }

        private LibGit2Repo GetSharedRepo()
        {
            lock (this.sharedRepoLock)
            {
                if (this.disposing)
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
    }
}
