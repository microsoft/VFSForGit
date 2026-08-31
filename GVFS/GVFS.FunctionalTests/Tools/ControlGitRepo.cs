using System;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public class ControlGitRepo
    {
        // Serializes creation and refresh of the machine-global shared cache across every
        // functional-test process running on this machine.
        private const string CacheMutexName = @"Global\GVFS.FunctionalTests.ControlGitRepoCache";

        static ControlGitRepo()
        {
            EnsureSharedCache();
        }

        private ControlGitRepo(string repoUrl, string rootPath, string commitish)
        {
            this.RootPath = rootPath;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;
        }

        public string RootPath { get; private set; }
        public string RepoUrl { get; private set; }
        public string Commitish { get; private set; }

        private static string CachePath
        {
            get { return Path.Combine(Properties.Settings.Default.ControlGitRepoRoot, "cache"); }
        }

        public static ControlGitRepo Create(string commitish = null)
        {
            string clonePath = Path.Combine(Properties.Settings.Default.ControlGitRepoRoot, Guid.NewGuid().ToString("N"));
            return new ControlGitRepo(
                GVFSTestConfig.RepoToClone,
                clonePath,
                commitish == null ? Properties.Settings.Default.Commitish : commitish);
        }

        //
        // IMPORTANT! These must parallel the settings in GVFSVerb:TrySetRequiredGitConfigSettings
        //
        public void Initialize()
        {
            const int MaxAttempts = 3;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    this.InitializeCore();
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts)
                {
                    // Building the control repo hit a transient failure (for example the shared
                    // cache was being rebuilt by another process). Discard the partial repo and
                    // retry from a clean directory.
                    Console.WriteLine($"ControlGitRepo.Initialize attempt {attempt} of {MaxAttempts} failed: {ex.Message}");
                    RepositoryHelpers.DeleteTestDirectory(this.RootPath);
                    Thread.Sleep(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        private void InitializeCore()
        {
            Directory.CreateDirectory(this.RootPath);
            GitProcess.Invoke(this.RootPath, "init");
            GitProcess.Invoke(this.RootPath, "config core.autocrlf false");
            GitProcess.Invoke(this.RootPath, "config core.editor true");
            GitProcess.Invoke(this.RootPath, "config merge.stat false");
            GitProcess.Invoke(this.RootPath, "config merge.renames false");
            GitProcess.Invoke(this.RootPath, "config advice.statusUoption false");
            GitProcess.Invoke(this.RootPath, "config core.abbrev 40");
            GitProcess.Invoke(this.RootPath, "config checkout.workers 0");
            GitProcess.Invoke(this.RootPath, "config core.useBuiltinFSMonitor false");
            GitProcess.Invoke(this.RootPath, "config pack.useSparse true");
            GitProcess.Invoke(this.RootPath, "config reset.quiet true");
            GitProcess.Invoke(this.RootPath, "config status.aheadbehind false");
            GitProcess.Invoke(this.RootPath, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RootPath, "config user.email \"functional@test.com\"");
            GitProcess.Invoke(this.RootPath, "remote add origin " + CachePath);
            this.Fetch(this.Commitish);
            GitProcess.Invoke(this.RootPath, "branch --set-upstream " + this.Commitish + " origin/" + this.Commitish);

            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.RootPath, "checkout " + this.Commitish);
            if (checkoutResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Control repo failed to checkout '{this.Commitish}'. The shared control-repo cache at '{CachePath}' is likely missing the branch. " +
                    $"git exit code {checkoutResult.ExitCode}: {checkoutResult.Errors}");
            }

            GitProcess.Invoke(this.RootPath, "branch --unset-upstream");

            // Enable the ORT merge strategy
            GitProcess.Invoke(this.RootPath, "config pull.twohead ort");
        }

        public void Fetch(string commitish)
        {
            ProcessResult result = InvokeGitWithRetry(this.RootPath, "fetch origin " + commitish);
            if (result.ExitCode != 0)
            {
                // Do not throw here. Some tests fetch a specific commit by SHA; whether the shared
                // cache can serve that SHA is a property of the cache, not of the test. The test's
                // own ValidateGitCommand (which compares the control repo against the GVFS repo)
                // is the correctness gate. Log for diagnosis and continue.
                Console.WriteLine(
                    $"ControlGitRepo.Fetch: 'fetch origin {commitish}' returned {result.ExitCode} from cache '{CachePath}': {result.Errors}");
            }
        }

        /// <summary>
        /// Creates or refreshes the shared bare cache that every control repo fetches from.
        /// </summary>
        /// <remarks>
        /// The cache path is machine-global and is shared by every functional-test fixture (fixtures
        /// run in parallel) and by concurrent test processes on the same machine. The previous
        /// implementation checked <see cref="Directory.Exists(string)"/> and then either cloned or
        /// fetched, and swallowed every git failure. That produced a flaky cascade: a transient
        /// clone or fetch failure, or a concurrent process that observed a half-built clone
        /// directory, left the cache missing branches. Every GitCommands test then failed its setup
        /// checkout with "pathspec ... did not match any file(s) known to git".
        ///
        /// This method serializes setup across processes with a system-wide mutex. It builds a
        /// missing cache atomically (clone into a temporary directory, verify the base branch, then
        /// move it into place). It never rebuilds or deletes an existing cache: on CI runners the
        /// cache is persistent and can hold commits from branches that no longer exist upstream
        /// (some tests fetch those commits by SHA), so replacing it with a fresh clone would drop
        /// those objects. An existing cache is only refreshed, best-effort. Finally it enables
        /// uploadpack.allowAnySHA1InWant so control repos can fetch any commit in the cache by SHA.
        /// </remarks>
        private static void EnsureSharedCache()
        {
            using (Mutex mutex = new Mutex(initiallyOwned: false, name: CacheMutexName))
            {
                bool mutexHeld = false;
                try
                {
                    try
                    {
                        mutexHeld = mutex.WaitOne(TimeSpan.FromMinutes(10));
                    }
                    catch (AbandonedMutexException)
                    {
                        // A previous process exited while holding the mutex. The cache is handled
                        // below regardless, so it is safe to proceed.
                        mutexHeld = true;
                    }

                    if (!mutexHeld)
                    {
                        throw new TimeoutException($"Timed out waiting to initialize the control-repo cache at '{CachePath}'.");
                    }

                    string baseBranch = Properties.Settings.Default.Commitish;

                    if (Directory.Exists(CachePath))
                    {
                        // Refresh the existing cache so newly-added test branches are available.
                        // Do this best-effort and never delete/rebuild: the persistent cache can
                        // hold commits that upstream no longer advertises (fetched by SHA by some
                        // tests), which a fresh clone would not restore.
                        InvokeGitWithRetry(CachePath, "fetch origin +refs/*:refs/*");
                    }
                    else
                    {
                        BuildFreshCache(baseBranch);
                    }

                    // Allow control repos to fetch any commit present in the cache by its SHA
                    // (some tests fetch specific commits directly). Without this, upload-pack
                    // rejects a SHA that is not an advertised ref tip with "not our ref".
                    ConfigureCacheForShaFetch(CachePath);
                }
                finally
                {
                    if (mutexHeld)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private static void BuildFreshCache(string baseBranch)
        {
            string root = Properties.Settings.Default.ControlGitRepoRoot;
            Directory.CreateDirectory(root);

            string tempCache = Path.Combine(root, "cache.tmp." + Guid.NewGuid().ToString("N"));

            ProcessResult clone = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (Directory.Exists(tempCache))
                {
                    RepositoryHelpers.DeleteTestDirectory(tempCache);
                }

                // Use --mirror (not --bare) so the cache carries the complete ref set. A --bare
                // clone copies only refs/heads/* and tags; some tests fetch commits (by SHA) that
                // live outside refs/heads, so a --bare cache would be missing those objects and the
                // fetch would fail with "not our ref". --mirror maps refs/*:refs/*, matching the
                // refresh path's "fetch origin +refs/*:refs/*".
                clone = GitProcess.InvokeProcess(
                    Environment.SystemDirectory,
                    "clone " + GVFSTestConfig.RepoToClone + " " + tempCache + " --mirror");

                if (clone.ExitCode == 0 && CacheHasBranch(tempCache, baseBranch))
                {
                    break;
                }

                if (attempt == 3)
                {
                    throw new InvalidOperationException(
                        $"Failed to build the control-repo cache from '{GVFSTestConfig.RepoToClone}' after {attempt} attempts. " +
                        $"git exit code {clone.ExitCode}: {clone.Errors}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(attempt * 2));
            }

            // Move the fully-built cache into place so no other process observes a partial directory.
            if (Directory.Exists(CachePath))
            {
                RepositoryHelpers.DeleteTestDirectory(CachePath);
            }

            Directory.Move(tempCache, CachePath);
        }

        private static void ConfigureCacheForShaFetch(string cachePath)
        {
            GitProcess.InvokeProcess(cachePath, "config uploadpack.allowAnySHA1InWant true");
            GitProcess.InvokeProcess(cachePath, "config uploadpack.allowReachableSHA1InWant true");
            GitProcess.InvokeProcess(cachePath, "config uploadpack.allowTipSHA1InWant true");
        }

        private static bool CacheHasBranch(string cachePath, string branch)
        {
            if (!Directory.Exists(cachePath))
            {
                return false;
            }

            ProcessResult result = GitProcess.InvokeProcess(cachePath, "rev-parse --verify --quiet refs/heads/" + branch);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
        }

        private static ProcessResult InvokeGitWithRetry(string workingDirectory, string command, int attempts = 3)
        {
            ProcessResult result = null;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                result = GitProcess.InvokeProcess(workingDirectory, command);
                if (result.ExitCode == 0)
                {
                    return result;
                }

                if (attempt < attempts)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(attempt));
                }
            }

            return result;
        }
    }
}
