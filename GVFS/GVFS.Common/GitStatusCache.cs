using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common
{
    /// <summary>
    /// Responsible for orchestrating the Git Status Cache interactions. This is a cache of the results of running
    /// the "git status" command.
    ///
    /// Consumers are responsible for invalidating the cache and directing it to rebuild.
    /// </summary>
    public class GitStatusCache : IDisposable
    {
        private const string EtwArea = nameof(GitStatusCache);
        private const int DelayBeforeRunningLoopAgainMs = 1000;

        private readonly TimeSpan backoffTime;

        // arbitrary value used when deciding whether to print
        // a message about a delayed status scan.
        private readonly TimeSpan delayThreshold = TimeSpan.FromSeconds(0.5);

        private string serializedGitStatusFilePath;

        /// <summary>
        /// The last time that the refresh loop noticed an
        /// invalidation.
        /// </summary>
        private DateTime lastInvalidationTime = DateTime.MinValue;

        /// <summary>
        /// This is the time the GitStatusCache started delaying refreshes.
        /// </summary>
        private DateTime initialDelayTime = DateTime.MinValue;

        private GVFSContext context;

        private AutoResetEvent wakeUpThread;
        private Task updateStatusCacheThread;
        private bool isStopping;
        private bool isInitialized;
        private StatusStatistics statistics;

        private volatile CacheState cacheState = CacheState.Dirty;

        private object cacheFileLock = new object();

        public GitStatusCache(GVFSContext context, GitStatusCacheConfig config)
            : this(context, config.BackoffTime)
        {
        }

        public GitStatusCache(GVFSContext context, TimeSpan backoffTime)
        {
            this.context = context;
            this.backoffTime = backoffTime;
            this.serializedGitStatusFilePath = this.context.Enlistment.GitStatusCachePath;
            this.statistics = new StatusStatistics();

            this.wakeUpThread = new AutoResetEvent(false);
        }

        public virtual void Initialize()
        {
            this.isInitialized = true;
            this.updateStatusCacheThread = Task.Factory.StartNew(this.SerializeStatusMainThread, TaskCreationOptions.LongRunning);
            this.Invalidate();
        }

        public virtual void Shutdown()
        {
            this.isStopping = true;

            if (this.isInitialized && this.updateStatusCacheThread != null)
            {
                this.wakeUpThread.Set();
                this.updateStatusCacheThread.Wait();
            }
        }

        /// <summary>
        /// Invalidate the status cache. Does not cause the cache to refresh
        /// If caller also wants to signal the refresh, they must call
        /// <see cref="RefreshAsynchronously" or cref="RefreshAndWait"/>.
        /// </summary>
        public virtual void Invalidate()
        {
            this.lastInvalidationTime = DateTime.UtcNow;
            this.cacheState = CacheState.Dirty;
        }

        public virtual bool IsCacheReadyAndUpToDate()
        {
            return this.cacheState == CacheState.Clean;
        }

        public virtual void RefreshAsynchronously()
        {
            this.wakeUpThread.Set();
        }

        public void RefreshAndWait()
        {
            this.RebuildStatusCacheIfNeeded(ignoreBackoff: true);
        }

        /// <summary>
        /// The GitStatusCache gets a chance to approve / deny requests for a
        /// command to take the GVFS lock. The GitStatusCache will only block
        /// if the command is a status command and there is a blocking error
        /// that might affect the correctness of the result.
        /// </summary>
        public virtual bool IsReadyForExternalAcquireLockRequests(
            NamedPipeMessages.LockData requester,
            out string infoMessage)
        {
            infoMessage = null;
            if (!this.isInitialized)
            {
                return true;
            }

            GitCommandLineParser gitCommand = new GitCommandLineParser(requester.ParsedCommand);
            if (!gitCommand.IsVerb(GitCommandLineParser.Verbs.Status) ||
                gitCommand.IsSerializedStatus())
            {
                return true;
            }

            bool shouldAllowExternalRequest = true;
            bool isCacheReady = false;

            lock (this.cacheFileLock)
            {
                if (this.IsCacheReadyAndUpToDate())
                {
                    isCacheReady = true;
                }
                else
                {
                    if (!this.TryDeleteStatusCacheFile())
                    {
                        shouldAllowExternalRequest = false;
                        infoMessage = string.Format("Unable to delete stale status cache file at: {0}", this.serializedGitStatusFilePath);
                    }
                }
            }

            if (isCacheReady)
            {
                this.statistics.RecordCacheReady();
            }
            else
            {
                this.statistics.RecordCacheNotReady();
            }

            if (!shouldAllowExternalRequest)
            {
                this.statistics.RecordBlockedRequest();
                this.context.Tracer.RelatedWarning("GitStatusCache.IsReadyForExternalAcquireLockRequests: request blocked");
            }

            return shouldAllowExternalRequest;
        }

        public virtual void Dispose()
        {
            this.Shutdown();

            if (this.wakeUpThread != null)
            {
                this.wakeUpThread.Dispose();
                this.wakeUpThread = null;
            }

            if (this.updateStatusCacheThread != null)
            {
                this.updateStatusCacheThread.Dispose();
                this.updateStatusCacheThread = null;
            }
        }

        public virtual bool WriteTelemetryandReset(EventMetadata metadata)
        {
            bool wroteTelemetry = false;
            if (!this.isInitialized)
            {
                return wroteTelemetry;
            }

            StatusStatistics statusStatistics = Interlocked.Exchange(ref this.statistics, new StatusStatistics());

            if (statusStatistics.BackgroundStatusScanCount > 0)
            {
                wroteTelemetry = true;
                metadata.Add("GitStatusCache.StatusScanCount", statusStatistics.BackgroundStatusScanCount);
            }

            if (statusStatistics.BackgroundStatusScanErrorCount > 0)
            {
                wroteTelemetry = true;
                metadata.Add("GitStatusCache.StatusScanErrorCount", statusStatistics.BackgroundStatusScanErrorCount);
            }

            if (statusStatistics.CacheReadyCount > 0)
            {
                wroteTelemetry = true;
                metadata.Add("GitStatusCache.CacheReadyCount", statusStatistics.CacheReadyCount);
            }

            if (statusStatistics.CacheNotReadyCount > 0)
            {
                wroteTelemetry = true;
                metadata.Add("GitStatusCache.CacheNotReadyCount", statusStatistics.CacheNotReadyCount);
            }

            if (statusStatistics.BlockedRequestCount > 0)
            {
                wroteTelemetry = true;
                metadata.Add("GitStatusCache.BlockedRequestCount", statusStatistics.BlockedRequestCount);
            }

            return wroteTelemetry;
        }

        private void SerializeStatusMainThread()
        {
            while (true)
            {
                try
                {
                    this.wakeUpThread.WaitOne();

                    if (this.isStopping)
                    {
                        break;
                    }

                    this.RebuildStatusCacheIfNeeded(ignoreBackoff: false);

                    // Delay to throttle the rate of how often status is run.
                    // Do not run status again for at least this timeout.
                    Thread.Sleep(DelayBeforeRunningLoopAgainMs);
                }
                catch (Exception ex)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    if (ex != null)
                    {
                        metadata.Add("Exception", ex.ToString());
                    }

                    this.context.Tracer.RelatedError(metadata, "Unhandled exception encountered on GitStatusCache background thread.");
                    Environment.Exit(1);
                }
            }
        }

        private void RebuildStatusCacheIfNeeded(bool ignoreBackoff)
        {
            bool needToRebuild = false;
            DateTime startTime;

            lock (this.cacheFileLock)
            {
                CacheState cacheState = this.cacheState;
                startTime = DateTime.UtcNow;

                if (cacheState == CacheState.Clean)
                {
                    this.context.Tracer.RelatedInfo("GitStatusCache.RebuildStatusCacheIfNeeded: Status Cache up-to-date.");
                }
                else if (!this.TryDeleteStatusCacheFile())
                {
                    // The cache is dirty, but we failed to delete the previous on disk cache.
                    // Do not rebuild the cache this time. Wait for the next invalidation
                    // to cause the thread to run again, or the on-disk cache will be deleted
                    // if a status command is run.
                }
                else if (!ignoreBackoff &&
                    (startTime - this.lastInvalidationTime) < this.backoffTime)
                {
                    // The approriate backoff time has not elapsed yet,
                    // If this is the 1st time we are delaying the background
                    // status scan (indicated by the initialDelayTime being set to
                    // DateTime.MinValue), mark the current time. We can then track
                    // how long the scan was delayed for.
                    if (this.initialDelayTime == DateTime.MinValue)
                    {
                        this.initialDelayTime = startTime;
                    }

                    // Signal the background thread to run again, so it
                    // can check if the backoff time has elapsed and it should
                    // rebuild the status cache.
                    this.wakeUpThread.Set();
                }
                else
                {
                    // The cache is dirty, and we succeeded in deleting the previous on disk cache and the minimum
                    // backoff time has passed, so now we can rebuild the status cache.
                    needToRebuild = true;
                }
            }

            if (needToRebuild)
            {
                this.statistics.RecordBackgroundStatusScanRun();

                bool rebuildStatusCacheSucceeded = this.TryRebuildStatusCache();

                TimeSpan delayedTime = startTime - this.initialDelayTime;
                TimeSpan statusRunTime = DateTime.UtcNow - startTime;

                string message = string.Format(
                    "GitStatusCache.RebuildStatusCacheIfNeeded: Done generating status. Cache state: {0}. Status scan time: {1:0.##}s.",
                    this.cacheState,
                    statusRunTime.TotalSeconds);
                if (delayedTime > this.backoffTime + this.delayThreshold)
                {
                    message += string.Format(" Status scan was delayed for: {0:0.##}s.", delayedTime.TotalSeconds);
                }

                this.context.Tracer.RelatedInfo(message);

                this.initialDelayTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Rebuild the status cache. This will run the background status to
        /// generate status results, and update the serialized status cache
        /// file.
        /// </summary>
        private bool TryRebuildStatusCache()
        {
            try
            {
                this.context.FileSystem.CreateDirectory(this.context.Enlistment.GitStatusCacheFolder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", EtwArea);
                metadata.Add("Exception", ex.ToString());

                this.context.Tracer.RelatedWarning(
                    metadata,
                    string.Format("GitStatusCache is unable to create git status cache folder at {0}.", this.context.Enlistment.GitStatusCacheFolder));
                return false;
            }

            // The status cache is regenerated on mount. This means that even if the write to temp file
            // and rename operation doesn't complete (due to a system crash), and there is a torn write,
            // GVFS is still protected because a new status cache file will be generated on mount.
            string tmpStatusFilePath = Path.Combine(this.context.Enlistment.GitStatusCacheFolder, Path.GetRandomFileName() + "_status.tmp");

            GitProcess.Result statusResult = null;

            // Do not modify this block unless you completely understand the comments and code within
            {
                // We MUST set the state to Rebuilding _immediately before_ we call the `git status` command. That allows us to
                // check afterwards if anything happened during the status command that should invalidate the cache, and we
                // can discard its results if that happens.
                this.cacheState = CacheState.Rebuilding;

                GitProcess git = this.context.Enlistment.CreateGitProcess();
                statusResult = git.SerializeStatus(
                    allowObjectDownloads: true,
                    serializePath: tmpStatusFilePath);
            }

            bool rebuildSucceeded = false;
            if (statusResult.ExitCodeIsSuccess)
            {
                lock (this.cacheFileLock)
                {
                    // Only update the cache if our state is still Rebuilding. Otherwise, this indicates that another call
                    // to Invalidate came in, and moved the state back to Dirty.
                    if (this.cacheState == CacheState.Rebuilding)
                    {
                        rebuildSucceeded = this.MoveCacheFileToFinalLocation(tmpStatusFilePath);
                        if (rebuildSucceeded)
                        {
                            // We have to check the state once again, because it could have been invalidated while we were
                            // copying the file in the previous step. Here we do it as a CompareExchange to minimize any further races.
                            if (Interlocked.CompareExchange(ref this.cacheState, CacheState.Clean, CacheState.Rebuilding) != CacheState.Rebuilding)
                            {
                                // We did not succeed in setting the state to Clean. Note that we have already overwritten the on disk cache,
                                // but all users of the cache file first check the cacheState, and since the cacheState is not Clean, no one
                                // should ever read it.

                                rebuildSucceeded = false;
                            }
                        }

                        if (!rebuildSucceeded)
                        {
                            this.cacheState = CacheState.Dirty;
                        }
                    }
                }

                if (!rebuildSucceeded)
                {
                    try
                    {
                        this.context.FileSystem.DeleteFile(tmpStatusFilePath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add("Exception", ex.ToString());

                        this.context.Tracer.RelatedError(
                            metadata,
                            string.Format("GitStatusCache is unable to delete temporary status cache file at {0}.", tmpStatusFilePath));
                    }
                }
            }
            else
            {
                this.statistics.RecordBackgroundStatusScanError();
                this.context.Tracer.RelatedInfo("GitStatusCache.TryRebuildStatusCache: Error generating status: {0}", statusResult.Errors);
            }

            return rebuildSucceeded;
        }

        private bool TryDeleteStatusCacheFile()
        {
            Debug.Assert(Monitor.IsEntered(this.cacheFileLock), "Attempting to delete the git status cache file without the cacheFileLock");

            try
            {
                if (this.context.FileSystem.FileExists(this.serializedGitStatusFilePath))
                {
                    this.context.FileSystem.DeleteFile(this.serializedGitStatusFilePath);
                }
            }
            catch (IOException ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // Unexpected, but maybe something deleted the file out from underneath us...
                // As the file is deleted, lets continue with the status generation..
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", EtwArea);
                metadata.Add("Exception", ex.ToString());

                this.context.Tracer.RelatedWarning(
                    metadata,
                    string.Format("GitStatusCache encountered exception attempting to delete cache file at {0}.", this.serializedGitStatusFilePath),
                    Keywords.Telemetry);

                return false;
            }

            return true;
        }

        /// <summary>
        /// Move (and overwrite) status cache file from the temporary location to the
        /// expected location for the status cache file.
        /// </summary>
        /// <returns>True on success, False on failure</returns>
        private bool MoveCacheFileToFinalLocation(string tmpStatusFilePath)
        {
            Debug.Assert(Monitor.IsEntered(this.cacheFileLock), "Attempting to update the git status cache file without the cacheFileLock");

            try
            {
                this.context.FileSystem.MoveAndOverwriteFile(tmpStatusFilePath, this.serializedGitStatusFilePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", EtwArea);
                metadata.Add("Exception", ex.ToString());

                this.context.Tracer.RelatedError(
                    metadata,
                    string.Format("GitStatusCache encountered exception attempting to update status cache file at {0} with {1}.", this.serializedGitStatusFilePath, tmpStatusFilePath));
            }

            return false;
        }

        private class StatusStatistics
        {
            public int BackgroundStatusScanCount { get; private set; }

            public int BackgroundStatusScanErrorCount { get; private set; }

            public int CacheReadyCount { get; private set; }

            public int CacheNotReadyCount { get; private set; }

            public int BlockedRequestCount { get; private set; }

            /// <summary>
            /// Record that a background status scan was run. This is the
            /// status command that is run to populate the status cache.
            /// </summary>
            public void RecordBackgroundStatusScanRun()
            {
                this.BackgroundStatusScanCount++;
            }

            /// <summary>
            /// Record that an error was encountered while running
            /// the background status scan.
            /// </summary>
            public void RecordBackgroundStatusScanError()
            {
                this.BackgroundStatusScanErrorCount++;
            }

            /// <summary>
            /// Record that a status command was run from the repository,
            /// and the cache was not ready to answer it.
            /// </summary>
            public void RecordCacheNotReady()
            {
                this.CacheNotReadyCount++;
            }

            /// <summary>
            /// Record that a status command was run from the repository,
            /// and the cache was ready to answer it.
            /// </summary>
            public void RecordCacheReady()
            {
                this.CacheReadyCount++;
            }

            /// <summary>
            /// Record that a status command was run from the repository,
            /// and the cache blocked the request. This only happens
            /// if there is a stale status cache file and it cannot be deleted.
            /// </summary>
            public void RecordBlockedRequest()
            {
                this.BlockedRequestCount++;
            }
        }

        // This should really be an enum, but because we need to CompareExchange it,
        // we have to create a reference type that looks like an enum instead.
        private class CacheState
        {
            public static readonly CacheState Dirty = new CacheState("Dirty");
            public static readonly CacheState Clean = new CacheState("Clean");
            public static readonly CacheState Rebuilding = new CacheState("Rebuilding");

            private string name;

            private CacheState(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return this.name;
            }
        }
    }
}
