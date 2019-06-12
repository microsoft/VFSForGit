using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    public partial class GVFSLock
    {
        private readonly object acquisitionLock = new object();
        private readonly ITracer tracer;
        private readonly LockHolder currentLockHolder = new LockHolder();

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
        /// <param name="requestor">The data for the external acquisition request.</param>
        /// <param name="existingExternalHolder">The current holder of the lock if the acquisition fails.</param>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        public bool TryAcquireLockForExternalRequestor(
            NamedPipeMessages.LockData requestor,
            out NamedPipeMessages.LockData existingExternalHolder)
        {
            EventMetadata metadata = new EventMetadata();
            EventLevel eventLevel = EventLevel.Verbose;
            metadata.Add("LockRequest", requestor.ToString());
            metadata.Add("IsElevated", requestor.IsElevated);

            existingExternalHolder = null;

            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.currentLockHolder.IsGVFS)
                    {
                        metadata.Add("CurrentLockHolder", "GVFS");
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    existingExternalHolder = this.GetExternalHolder();
                    if (existingExternalHolder != null)
                    {
                        metadata.Add("CurrentLockHolder", existingExternalHolder.ToString());
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    metadata.Add("Result", "Accepted");
                    eventLevel = EventLevel.Informational;

                    this.currentLockHolder.AcquireForExternalRequestor(requestor);
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
        public bool TryAcquireLockForGVFS()
        {
            EventMetadata metadata = new EventMetadata();
            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.currentLockHolder.IsGVFS)
                    {
                        return true;
                    }

                    NamedPipeMessages.LockData existingExternalHolder = this.GetExternalHolder();
                    if (existingExternalHolder != null)
                    {
                        metadata.Add("CurrentLockHolder", existingExternalHolder.ToString());
                        metadata.Add("Result", "Denied");
                        return false;
                    }

                    this.currentLockHolder.AcquireForGVFS();
                    metadata.Add("Result", "Accepted");
                    return true;
                }
            }
            finally
            {
                this.tracer.RelatedEvent(EventLevel.Verbose, "TryAcquireLockInternal", metadata);
            }
        }

        public void ReleaseLockHeldByGVFS()
        {
            lock (this.acquisitionLock)
            {
                if (!this.currentLockHolder.IsGVFS)
                {
                    throw new InvalidOperationException("Cannot release lock that is not held by GVFS");
                }

                this.tracer.RelatedEvent(EventLevel.Verbose, nameof(this.ReleaseLockHeldByGVFS), new EventMetadata());
                this.currentLockHolder.Release();
            }
        }

        public bool ReleaseLockHeldByExternalProcess(int pid)
        {
            return this.ReleaseExternalLock(pid, nameof(this.ReleaseLockHeldByExternalProcess));
        }

        public NamedPipeMessages.LockData GetExternalHolder()
        {
            NamedPipeMessages.LockData externalHolder;
            this.IsLockAvailable(checkExternalHolderOnly: true, existingExternalHolder: out externalHolder);

            return externalHolder;
        }

        public bool IsLockAvailableForExternalRequestor(out NamedPipeMessages.LockData existingExternalHolder)
        {
            return this.IsLockAvailable(checkExternalHolderOnly: false, existingExternalHolder: out existingExternalHolder);
        }

        public string GetLockedGitCommand()
        {
            // In this code path, we don't care if the process terminated without releasing the lock. The calling code
            // is asking us about this lock so that it can determine if git was the cause of certain IO events. Even
            // if the git process has terminated, the answer to that question does not change.
            NamedPipeMessages.LockData currentHolder = this.currentLockHolder.GetExternalHolder();

            if (currentHolder != null)
            {
                return currentHolder.ParsedCommand;
            }

            return null;
        }

        public string GetStatus()
        {
            lock (this.acquisitionLock)
            {
                if (this.currentLockHolder.IsGVFS)
                {
                    return "Held by GVFS.";
                }

                NamedPipeMessages.LockData externalHolder = this.GetExternalHolder();
                if (externalHolder != null)
                {
                    return string.Format("Held by {0} (PID:{1})", externalHolder.ParsedCommand, externalHolder.PID);
                }
            }

            return "Free";
        }

        private bool IsLockAvailable(bool checkExternalHolderOnly, out NamedPipeMessages.LockData existingExternalHolder)
        {
            lock (this.acquisitionLock)
            {
                if (!checkExternalHolderOnly &&
                    this.currentLockHolder.IsGVFS)
                {
                    existingExternalHolder = null;
                    return false;
                }

                bool externalHolderTerminatedWithoutReleasingLock;
                existingExternalHolder = this.currentLockHolder.GetExternalHolder(
                    out externalHolderTerminatedWithoutReleasingLock);

                if (externalHolderTerminatedWithoutReleasingLock)
                {
                    this.ReleaseLockForTerminatedProcess(existingExternalHolder.PID);
                    this.tracer.SetGitCommandSessionId(string.Empty);
                    existingExternalHolder = null;
                }

                return existingExternalHolder == null;
            }
        }

        private bool ReleaseExternalLock(int pid, string eventName)
        {
            lock (this.acquisitionLock)
            {
                EventMetadata metadata = new EventMetadata();

                try
                {
                    if (this.currentLockHolder.IsGVFS)
                    {
                        metadata.Add("IsLockedByGVFS", "true");
                        return false;
                    }

                    // We don't care if the process has already terminated. We're just trying to record the info for the last holder.
                    NamedPipeMessages.LockData previousExternalHolder = this.currentLockHolder.GetExternalHolder();

                    if (previousExternalHolder == null)
                    {
                        metadata.Add("Result", "Failed (no current holder, requested PID=" + pid + ")");
                        return false;
                    }

                    metadata.Add("CurrentLockHolder", previousExternalHolder.ToString());
                    metadata.Add("IsElevated", previousExternalHolder.IsElevated);
                    metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);

                    if (previousExternalHolder.PID != pid)
                    {
                        metadata.Add("pid", pid);
                        metadata.Add("Result", "Failed (wrong PID)");
                        return false;
                    }

                    this.currentLockHolder.Release();
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

            private long placeholderTotalUpdateTimeMs;
            private long placeholderUpdateFilesTimeMs;
            private long placeholderUpdateFoldersTimeMs;
            private long placeholderWriteAndFlushTimeMs;
            private int deleteFolderPlacehoderAttempted;
            private int folderPlaceholdersDeleted;
            private int folderPlaceholdersPathNotFound;
            private long parseGitIndexTimeMs;
            private long projectionWriteLockHeldMs;

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

            public void RecordUpdatePlaceholders(
                long durationMs,
                long updateFilesMs,
                long updateFoldersMs,
                long writeAndFlushMs,
                int deleteFolderPlacehoderAttempted,
                int folderPlaceholdersDeleted,
                int folderPlaceholdersPathNotFound)
            {
                this.placeholderTotalUpdateTimeMs = durationMs;
                this.placeholderUpdateFilesTimeMs = updateFilesMs;
                this.placeholderUpdateFoldersTimeMs = updateFoldersMs;
                this.placeholderWriteAndFlushTimeMs = writeAndFlushMs;
                this.deleteFolderPlacehoderAttempted = deleteFolderPlacehoderAttempted;
                this.folderPlaceholdersDeleted = folderPlaceholdersDeleted;
                this.folderPlaceholdersPathNotFound = folderPlaceholdersPathNotFound;
            }

            public void RecordProjectionWriteLockHeld(long durationMs)
            {
                this.projectionWriteLockHeldMs = durationMs;
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
                metadata.Add("UpdatePlaceholdersMS", this.placeholderTotalUpdateTimeMs);
                metadata.Add("UpdateFilePlaceholdersMS", this.placeholderUpdateFilesTimeMs);
                metadata.Add("UpdateFolderPlaceholdersMS", this.placeholderUpdateFoldersTimeMs);
                metadata.Add("DeleteFolderPlacehoderAttempted", this.deleteFolderPlacehoderAttempted);
                metadata.Add("FolderPlaceholdersDeleted", this.folderPlaceholdersDeleted);
                metadata.Add("FolderPlaceholdersPathNotFound", this.folderPlaceholdersPathNotFound);
                metadata.Add("PlaceholdersWriteAndFlushMS", this.placeholderWriteAndFlushTimeMs);
                metadata.Add("ProjectionWriteLockHeldMs", this.projectionWriteLockHeldMs);

                metadata.Add("BlobsDownloaded", this.numBlobs);
                metadata.Add("BlobDownloadTimeMS", this.blobDownloadTimeMs);

                metadata.Add("CommitsAndTreesDownloaded", this.numCommitsAndTrees);
                metadata.Add("CommitsAndTreesDownloadTimeMS", this.commitAndTreeDownloadTimeMs);

                metadata.Add("SizeQueries", this.numSizeQueries);
                metadata.Add("SizeQueryTimeMS", this.sizeQueryTimeMs);
            }
        }

        /// <summary>
        /// This class manages the state of which process currently owns the GVFS lock. This code is complicated because
        /// the lock can be held by us or by an external process, and because the external process that holds the lock
        /// can terminate without releasing the lock. If that happens, we implicitly release the lock the next time we
        /// check to see who is holding it.
        ///
        /// The goal of this class is to make it impossible for the rest of GVFSLock to read the external holder without being
        /// aware of the fact that it could have terminated.
        ///
        /// This class assumes that the caller is handling all synchronization.
        /// </summary>
        private class LockHolder
        {
            private NamedPipeMessages.LockData externalLockHolder;

            public bool IsFree
            {
                get { return !this.IsGVFS && this.externalLockHolder == null; }
            }

            public bool IsGVFS
            {
                get; private set;
            }

            public void AcquireForGVFS()
            {
                if (this.externalLockHolder != null)
                {
                    throw new InvalidOperationException("Cannot acquire for GVFS because there is an external holder");
                }

                this.IsGVFS = true;
            }

            public void AcquireForExternalRequestor(NamedPipeMessages.LockData externalLockHolder)
            {
                if (this.IsGVFS ||
                    this.externalLockHolder != null)
                {
                    throw new InvalidOperationException("Cannot acquire a lock that is already held");
                }

                this.externalLockHolder = externalLockHolder;
            }

            public void Release()
            {
                this.IsGVFS = false;
                this.externalLockHolder = null;
            }

            public NamedPipeMessages.LockData GetExternalHolder()
            {
                return this.externalLockHolder;
            }

            public NamedPipeMessages.LockData GetExternalHolder(out bool externalHolderTerminatedWithoutReleasingLock)
            {
                externalHolderTerminatedWithoutReleasingLock = false;

                if (this.externalLockHolder != null)
                {
                    int pid = this.externalLockHolder.PID;
                    externalHolderTerminatedWithoutReleasingLock = !GVFSPlatform.Instance.IsProcessActive(pid);
                }

                return this.externalLockHolder;
            }
        }
    }
}
