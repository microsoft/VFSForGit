using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    public partial class GVFSLock : IDisposable
    {
        private readonly object acquisitionLock = new object();

        private readonly ITracer tracer;
        private NamedPipeMessages.LockData lockHolder;

        private ManualResetEvent externalLockReleased;

        private Stats stats;

        public GVFSLock(ITracer tracer)
        {
            this.tracer = tracer;
            this.externalLockReleased = new ManualResetEvent(initialState: true);
            this.stats = new Stats();
        }

        public bool IsLockedByGVFS
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
                    if (this.IsLockedByGVFS)
                    {
                        metadata.Add("CurrentLockHolder", "GVFS");
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    if (this.IsExternalLockHolderAlive() &&
                        this.lockHolder.PID != requester.PID)
                    {
                        metadata.Add("CurrentLockHolder", this.lockHolder.ToString());
                        metadata.Add("Result", "Denied");

                        holder = this.lockHolder;
                        return false;
                    }
                    
                    metadata.Add("Result", "Accepted");
                    eventLevel = EventLevel.Informational;

                    this.lockHolder = requester;
                    this.externalLockReleased.Reset();
                    this.stats = new Stats();

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
                    if (this.IsLockedByGVFS)
                    {
                        return true;
                    }

                    if (this.IsExternalLockHolderAlive())
                    {
                        metadata.Add("CurrentLockHolder", this.lockHolder.ToString());
                        metadata.Add("Result", "Denied");
                        return false;
                    }

                    this.IsLockedByGVFS = true;
                    this.externalLockReleased.Set();
                    metadata.Add("Result", "Accepted");
                    return true;
                }
            }
            finally
            {
                this.tracer.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", metadata);
            }
        }

        public void RecordObjectDownload(bool isBlob, long downloadTimeMs)
        {
            this.stats.RecordObjectDownload(isBlob, downloadTimeMs);
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
            this.IsLockedByGVFS = false;
        }

        public bool ReleaseExternalLock(int pid)
        {
            return this.ReleaseExternalLock(pid, nameof(this.ReleaseExternalLock));
        }

        public bool WaitOnExternalLockRelease(int millisecondsTimeout)
        {
            return this.externalLockReleased.WaitOne(millisecondsTimeout);
        }

        public bool IsExternalLockHolderAlive()
        {
            lock (this.acquisitionLock)
            {
                if (this.lockHolder == null)
                {
                    return false;
                }

                Process process = null;
                try
                {
                    int pid = this.lockHolder.PID;
                    if (ProcessHelper.TryGetProcess(pid, out process))
                    {
                        return true;
                    }

                    this.ReleaseLockForTerminatedProcess(pid);
                    return false;
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
            return this.lockHolder;
        }

        public string GetLockedGitCommand()
        {
            NamedPipeMessages.LockData currentHolder = this.lockHolder;
            if (currentHolder != null)
            {
                return currentHolder.ParsedCommand;
            }

            return null;
        }

        public string GetStatus()
        {
            if (this.IsLockedByGVFS)
            {
                return "Held by GVFS.";
            }

            NamedPipeMessages.LockData currentHolder = this.lockHolder;
            if (currentHolder != null)
            {
                return string.Format("Held by {0} (PID:{1})", currentHolder.ParsedCommand, currentHolder.PID);
            }

            return "Free";
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.externalLockReleased != null)
                {
                    this.externalLockReleased.Dispose();
                    this.externalLockReleased = null;
                }
            }
        }

        private bool ReleaseExternalLock(int pid, string eventName)
        {
            lock (this.acquisitionLock)
            {
                EventMetadata metadata = new EventMetadata();

                try
                {
                    if (this.IsLockedByGVFS)
                    {
                        metadata.Add("IsLockedByGVFS", "true");
                        return false;
                    }

                    if (this.lockHolder == null)
                    {
                        metadata.Add("Result", "Failed (no current holder, requested PID=" + pid + ")");
                        return false;
                    }

                    metadata.Add("CurrentLockHolder", this.lockHolder.ToString());
                    metadata.Add("IsElevated", this.lockHolder.IsElevated);

                    if (this.lockHolder.PID != pid)
                    {
                        metadata.Add("pid", pid);
                        metadata.Add("Result", "Failed (wrong PID)");
                        return false;
                    }

                    this.lockHolder = null;
                    this.externalLockReleased.Set();
                    metadata.Add("Result", "Released");
                    this.stats.AddStatsToTelemetry(metadata);

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

        public class GVFSLockException : Exception
        {
            public GVFSLockException(string message)
                : base(message)
            {
            }
        }

        // The lock release event is a convenient place to record stats about things that happened while a git command was running,
        // such as duration/count of object downloads during a git command, cache hits during a git command, etc.
        private class Stats
        {
            private Stopwatch lockAcquiredTime;
            private int numBlobs;
            private long blobDownloadTime;
            private int numCommitsAndTrees;
            private long commitAndTreeDownloadTime;

            public Stats()
            {
                this.lockAcquiredTime = Stopwatch.StartNew();
            }

            public void RecordObjectDownload(bool isBlob, long downloadTimeMs)
            {
                if (isBlob)
                {
                    Interlocked.Increment(ref this.numBlobs);
                    Interlocked.Add(ref this.blobDownloadTime, downloadTimeMs);
                }
                else
                {
                    Interlocked.Increment(ref this.numCommitsAndTrees);
                    Interlocked.Add(ref this.commitAndTreeDownloadTime, downloadTimeMs);
                }
            }

            public void AddStatsToTelemetry(EventMetadata metadata)
            {
                metadata.Add("DurationMS", this.lockAcquiredTime.ElapsedMilliseconds);
                metadata.Add("BlobsDownloaded", this.numBlobs);
                metadata.Add("BlobDownloadTimeMS", this.blobDownloadTime);
                metadata.Add("CommitsAndTreesDownloaded", this.numCommitsAndTrees);
                metadata.Add("CommitsAndTreesDownloadTimeMS", this.commitAndTreeDownloadTime);
            }
        }
    }
}