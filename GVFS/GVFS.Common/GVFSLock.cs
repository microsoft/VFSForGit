using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    public class GVFSLock : IDisposable
    {
        private readonly object acquisitionLock = new object();

        private readonly ITracer tracer;
        private readonly ProcessWatcher processWatcher;
        private NamedPipeMessages.LockData lockHolder;

        private ManualResetEvent externalLockReleased;

        public GVFSLock(ITracer tracer)
        {
            this.tracer = tracer;
            this.externalLockReleased = new ManualResetEvent(initialState: true);
            this.processWatcher = new ProcessWatcher(this.ReleaseLockForTerminatedProcess);
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
        /// <param name="holder">
        /// The current holder of the lock if the acquisition fails, or
        /// the input request if it succeeds.
        /// </param>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        public bool TryAcquireLock(
            NamedPipeMessages.LockData requester,
            out NamedPipeMessages.LockData holder)
        {
            EventMetadata metadata = new EventMetadata();
            EventLevel eventLevel = EventLevel.Verbose;
            metadata.Add("LockRequest", requester.ToString());
            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.IsLockedByGVFS)
                    {
                        holder = null;
                        metadata.Add("CurrentLockHolder", "GVFS");
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    if (this.lockHolder != null &&
                        this.lockHolder.PID != requester.PID)
                    {
                        holder = this.lockHolder;

                        metadata.Add("CurrentLockHolder", this.lockHolder.ToString());
                        metadata.Add("Result", "Denied");
                        return false;
                    }
                    
                    metadata.Add("Result", "Accepted");
                    eventLevel = EventLevel.Informational;

                    Process process;
                    if (ProcessHelper.TryGetProcess(requester.PID, out process))
                    {
                        this.processWatcher.WatchForTermination(requester.PID, GVFSConstants.CommandParentExePrefix);

                        process.Dispose();
                        this.lockHolder = requester;
                        holder = requester;
                        this.externalLockReleased.Reset();

                        return true;
                    }
                    else
                    {
                        // Process is no longer running so let it 
                        // succeed since the process non-existence
                        // signals the lock release.
                        if (process != null)
                        {
                            process.Dispose();
                        }

                        holder = null;
                        return true;
                    }
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

                    if (this.lockHolder != null)
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
            lock (this.acquisitionLock)
            {
                this.IsLockedByGVFS = false;
            }
        }

        public bool ReleaseExternalLock(int pid)
        {
            return this.ReleaseExternalLock(pid, nameof(this.ReleaseExternalLock));
        }

        public bool WaitOnExternalLockRelease(int millisecondsTimeout)
        {
            return this.externalLockReleased.WaitOne(millisecondsTimeout);
        }

        /// <summary>
        /// Returns true if the lock is currently held by an external
        /// caller that represents a git call using one of the specified git verbs.
        /// </summary>
        public bool IsLockedByGitVerb(params string[] verbs)
        {
            string command = this.GetLockedGitCommand();
            if (!string.IsNullOrEmpty(command))
            {
                return GitHelper.IsVerb(command, verbs);
            }

            return false;
        }

        public bool IsExternalLockHolderAlive()
        {
            lock (this.acquisitionLock)
            {
                return this.lockHolder != null;
            }
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

            lock (this.acquisitionLock)
            {
                string lockedCommand = this.GetLockedGitCommand();
                if (!string.IsNullOrEmpty(lockedCommand))
                {
                    return string.Format("Held by {0} (PID:{1})", lockedCommand, this.lockHolder.PID);
                }
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

                    if (this.lockHolder.PID != pid)
                    {
                        metadata.Add("Result", "Failed (wrong PID)");
                        return false;
                    }

                    this.lockHolder = null;
                    this.processWatcher.StopWatching(pid);
                    this.externalLockReleased.Set();
                    metadata.Add("Result", "Released");
                    return true;
                }
                finally
                {
                    this.tracer.RelatedEvent(EventLevel.Informational, eventName, metadata);
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
    }
}