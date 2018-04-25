using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    public partial class GVFSLock
    {
        private readonly object acquisitionLock = new object();

        private readonly ITracer tracer;

        private bool isLockedByGVFS;
        private NamedPipeMessages.LockData externalLockHolder;

        public GVFSLock(ITracer tracer)
        {
            this.tracer = tracer;
            this.Stats = new ActiveGitCommandStats();
        }

        public ActiveGitCommandStats Stats
        {
            get;
            private set;
        }

        /// <summary>
        /// Allows external callers (non-GVFS) to acquire the lock.
        /// </summary>
        /// <param name="requester">The data for the external acquisition request.</param>
        /// <param name="holder">The current holder of the lock if the acquisition fails.</param>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        public bool TryAcquireLock(
            NamedPipeMessages.LockData requester,
            out NamedPipeMessages.LockData holder)
        {
            EventMetadata metadata = new EventMetadata();
            EventLevel eventLevel = EventLevel.Verbose;
            metadata.Add("LockRequest", requester.ToString());
            metadata.Add("IsElevated", requester.IsElevated);

            holder = null;

            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.isLockedByGVFS)
                    {
                        metadata.Add("CurrentLockHolder", "GVFS");
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    if (!this.IsLockAvailable(checkExternalHolderOnly: true))
                    {
                        metadata.Add("CurrentLockHolder", this.externalLockHolder.ToString());
                        metadata.Add("Result", "Denied");

                        holder = this.externalLockHolder;
                        return false;
                    }
                    
                    metadata.Add("Result", "Accepted");
                    eventLevel = EventLevel.Informational;

                    this.externalLockHolder = requester;
                    this.Stats = new ActiveGitCommandStats();

                    return true;
                }
            }
            finally
            {
                this.tracer.RelatedEvent(eventLevel, "TryAcquireLockExternal", metadata);
            }
        }

        /// <summary>
        /// Allow GVFS to acquire the lock.
        /// </summary>
        /// <returns>True if GVFS was able to acquire the lock or if it already held it. False othwerwise.</returns>
        public bool TryAcquireLock()
        {
            EventMetadata metadata = new EventMetadata();
            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.isLockedByGVFS)
                    {
                        return true;
                    }

                    if (!this.IsLockAvailable(checkExternalHolderOnly: true))
                    {
                        metadata.Add("CurrentLockHolder", this.externalLockHolder.ToString());
                        metadata.Add("Result", "Denied");
                        return false;
                    }

                    this.isLockedByGVFS = true;
                    metadata.Add("Result", "Accepted");
                    return true;
                }
            }
            finally
            {
                this.tracer.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", metadata);
            }
        }

        /// <summary>
        /// Allow GVFS to release the lock if it holds it.
        /// </summary>
        /// <remarks>
        /// This should only be invoked by GVFS and not external callers. 
        /// Release by external callers is implicit on process termination.
        /// </remarks>
        public void ReleaseLock()
        {
            this.tracer.RelatedEvent(EventLevel.Verbose, "ReleaseLock", new EventMetadata());
            this.isLockedByGVFS = false;
        }

        public bool ReleaseExternalLock(int pid)
        {
            return this.ReleaseExternalLock(pid, nameof(this.ReleaseExternalLock));
        }

        public bool IsLockAvailable(bool checkExternalHolderOnly = false)
        {
            lock (this.acquisitionLock)
            {
                if (!checkExternalHolderOnly &&
                    this.isLockedByGVFS)
                {
                    return false;
                }

                if (this.externalLockHolder == null)
                {
                    return true;
                }

                Process process = null;
                try
                {
                    int pid = this.externalLockHolder.PID;
                    if (ProcessHelper.TryGetProcess(pid, out process))
                    {
                        return false;
                    }

                    this.ReleaseLockForTerminatedProcess(pid);
                    return true;
                }
                finally
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }
            }
        }

        public NamedPipeMessages.LockData GetExternalLockHolder()
        {
            return this.externalLockHolder;
        }

        public string GetLockedGitCommand()
        {
            NamedPipeMessages.LockData currentHolder = this.externalLockHolder;
            if (currentHolder != null)
            {
                return currentHolder.ParsedCommand;
            }

            return null;
        }

        public string GetStatus()
        {
            if (this.isLockedByGVFS)
            {
                return "Held by GVFS.";
            }

            NamedPipeMessages.LockData currentHolder = this.externalLockHolder;
            if (currentHolder != null)
            {
                return string.Format("Held by {0} (PID:{1})", currentHolder.ParsedCommand, currentHolder.PID);
            }

            return "Free";
        }

        private bool ReleaseExternalLock(int pid, string eventName)
        {
            lock (this.acquisitionLock)
            {
                EventMetadata metadata = new EventMetadata();

                try
                {
                    if (this.isLockedByGVFS)
                    {
                        metadata.Add("IsLockedByGVFS", "true");
                        return false;
                    }

                    if (this.externalLockHolder == null)
                    {
                        metadata.Add("Result", "Failed (no current holder, requested PID=" + pid + ")");
                        return false;
                    }

                    metadata.Add("CurrentLockHolder", this.externalLockHolder.ToString());
                    metadata.Add("IsElevated", this.externalLockHolder.IsElevated);
                    metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);

                    if (this.externalLockHolder.PID != pid)
                    {
                        metadata.Add("pid", pid);
                        metadata.Add("Result", "Failed (wrong PID)");
                        return false;
                    }

                    this.externalLockHolder = null;
                    metadata.Add("Result", "Released");
                    this.Stats.AddStatsToTelemetry(metadata);

                    return true;
                }
                finally
                {
                    this.tracer.RelatedEvent(EventLevel.Informational, eventName, metadata, Keywords.Telemetry);
                }
            }
        }

        private void ReleaseLockForTerminatedProcess(int pid)
        {
            this.ReleaseExternalLock(pid, "ExternalLockHolderExited");
        }

        // The lock release event is a convenient place to record stats about things that happened while a git command was running,
        // such as duration/count of object downloads during a git command, cache hits during a git command, etc.
        public class ActiveGitCommandStats
        {
            private Stopwatch lockAcquiredTime;
            private long lockHeldExternallyTimeMs;

            private long placeholderUpdateTimeMs;
            private long parseGitIndexTimeMs;

            private int numBlobs;
            private long blobDownloadTimeMs;

            private int numCommitsAndTrees;
            private long commitAndTreeDownloadTimeMs;

            private int numSizeQueries;
            private long sizeQueryTimeMs;

            public ActiveGitCommandStats()
            {
                this.lockAcquiredTime = Stopwatch.StartNew();
            }

            public void RecordReleaseExternalLockRequested()
            {
                this.lockHeldExternallyTimeMs = this.lockAcquiredTime.ElapsedMilliseconds;
            }

            public void RecordUpdatePlaceholders(long durationMs)
            {
                this.placeholderUpdateTimeMs = durationMs;
            }

            public void RecordParseGitIndex(long durationMs)
            {
                this.parseGitIndexTimeMs = durationMs;
            }

            public void RecordObjectDownload(bool isBlob, long downloadTimeMs)
            {
                if (isBlob)
                {
                    Interlocked.Increment(ref this.numBlobs);
                    Interlocked.Add(ref this.blobDownloadTimeMs, downloadTimeMs);
                }
                else
                {
                    Interlocked.Increment(ref this.numCommitsAndTrees);
                    Interlocked.Add(ref this.commitAndTreeDownloadTimeMs, downloadTimeMs);
                }
            }

            public void RecordSizeQuery(long queryTimeMs)
            {
                Interlocked.Increment(ref this.numSizeQueries);
                Interlocked.Add(ref this.sizeQueryTimeMs, queryTimeMs);
            }

            public void AddStatsToTelemetry(EventMetadata metadata)
            {
                metadata.Add("DurationMS", this.lockAcquiredTime.ElapsedMilliseconds);
                metadata.Add("LockHeldExternallyMS", this.lockHeldExternallyTimeMs);
                metadata.Add("ParseGitIndexMS", this.parseGitIndexTimeMs);
                metadata.Add("UpdatePlaceholdersMS", this.placeholderUpdateTimeMs);

                metadata.Add("BlobsDownloaded", this.numBlobs);
                metadata.Add("BlobDownloadTimeMS", this.blobDownloadTimeMs);

                metadata.Add("CommitsAndTreesDownloaded", this.numCommitsAndTrees);
                metadata.Add("CommitsAndTreesDownloadTimeMS", this.commitAndTreeDownloadTimeMs);

                metadata.Add("SizeQueries", this.numSizeQueries);
                metadata.Add("SizeQueryTimeMS", this.sizeQueryTimeMs);
            }
        }
    }
}