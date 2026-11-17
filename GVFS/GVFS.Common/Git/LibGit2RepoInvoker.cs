using GVFS.Common.Tracing;
using System;
using System.Collections;
using System.IO;
using System.Threading;

namespace GVFS.Common.Git
{
    public class LibGit2RepoInvoker : IDisposable
    {
        private readonly Func<LibGit2Repo> createRepo;
        private readonly LibGit2Repo.TryReadConfigBoolWithGit readBoolWithGit;
        private readonly IDictionary environment;
        private readonly ITracer tracer;
        private readonly Lock sharedRepoLock = new Lock();
        private volatile bool disposing;
        private volatile int activeCallers;
        private LibGit2Repo sharedRepo;

        /// <param name="readBoolWithGit">
        /// Reads a key with git.exe. <see cref="GetConfigBoolOrDefault"/> uses it instead of libgit2
        /// when <see cref="LibGit2Repo.EnvironmentChangesGitConfig()"/> is true.
        /// </param>
        /// <param name="environment">The environment to check. Null selects the process environment.</param>
        public LibGit2RepoInvoker(ITracer tracer, Func<LibGit2Repo> createRepo, LibGit2Repo.TryReadConfigBoolWithGit readBoolWithGit, IDictionary environment = null)
        {
            this.tracer = tracer;
            this.createRepo = createRepo;
            this.readBoolWithGit = readBoolWithGit ?? throw new ArgumentNullException(nameof(readBoolWithGit));
            this.environment = environment;

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

        /// <summary>
        /// Returns <paramref name="defaultValue"/> when the key is unset. Throws when the read fails,
        /// so that a malformed kill switch does not silently select the default.
        /// </summary>
        public bool GetConfigBoolOrDefault(string key, bool defaultValue)
        {
            if (LibGit2Repo.EnvironmentChangesGitConfig(this.environment ?? Environment.GetEnvironmentVariables()))
            {
                if (!this.readBoolWithGit(key, out bool? gitValue, out string error))
                {
                    throw new InvalidDataException(error);
                }

                return gitValue ?? defaultValue;
            }

            bool? value = defaultValue;
            if (this.TryInvoke(repo => repo.GetConfigBool(key), out value))
            {
                return value ?? defaultValue;
            }

            return defaultValue;
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
