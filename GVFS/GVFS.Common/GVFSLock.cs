using System;
using System.Diagnostics;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;

namespace GVFS.Common
{
    public class GVFSLock
    {
        private readonly object acquisitionLock = new object();

        private readonly ITracer tracer;
        private NamedPipeMessages.AcquireLock.Data lockHolder;

        private bool isHeldInternally;

        public GVFSLock(ITracer tracer)
        {
            this.tracer = tracer;
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
            NamedPipeMessages.AcquireLock.Data requester,
            out NamedPipeMessages.AcquireLock.Data holder)
        {
            EventMetadata metadata = new EventMetadata();
            EventLevel eventLevel = EventLevel.Verbose;
            metadata.Add("LockRequest", requester.ToString());
            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.isHeldInternally)
                    {
                        holder = null;
                        metadata.Add("CurrentLockHolder", "GVFS");
                        metadata.Add("Result", "Denied");

                        return false;
                    }

                    if (this.IsExternalProcessAlive() &&
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
                    if (ProcessHelper.TryGetProcess(requester.PID, out process) &&
                        string.Equals(requester.OriginalCommand, ProcessHelper.GetCommandLine(process)))
                    {
                        this.lockHolder = requester;
                        holder = requester;

                        return true;
                    }
                    else
                    {
                        // Process is no longer running so let it 
                        // succeed since the process non-existence
                        // signals the lock release.
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
            EventLevel eventLevel = EventLevel.Verbose;
            try
            {
                lock (this.acquisitionLock)
                {
                    if (this.IsExternalProcessAlive())
                    {
                        metadata.Add("CurrentLockHolder", this.lockHolder.ToString());
                        metadata.Add("Full Command", this.lockHolder.OriginalCommand);
                        metadata.Add("Result", "Denied");
                        return false;
                    }

                    this.ClearHolder();
                    this.isHeldInternally = true;
                    metadata.Add("Result", "Accepted");
                    eventLevel = EventLevel.Informational;
                    return true;
                }
            }
            finally
            {
                this.tracer.RelatedEvent(eventLevel, "TryAcquireLockInternal", metadata);
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
                this.isHeldInternally = false;
            }
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

        public string GetLockedGitCommand()
        {
            if (this.IsExternalProcessAlive())
            {
                return this.lockHolder.ParsedCommand;
            }

            return null;
        }

        public string GetStatus()
        {
            if (this.isHeldInternally)
            {
                return "Held by GVFS.";
            }

            string lockedCommand = this.GetLockedGitCommand();
            if (!string.IsNullOrEmpty(lockedCommand))
            {
                return string.Format("Held by {0} (PID:{1})", lockedCommand, this.lockHolder.PID);
            }

            return "Free";
        }

        private void ClearHolder()
        {
            this.lockHolder = null;
        }

        private bool IsExternalProcessAlive()
        {
            lock (this.acquisitionLock)
            {
                if (this.isHeldInternally)
                {
                    if (this.lockHolder != null)
                    {
                        throw new InvalidOperationException("Inconsistent GVFSLock state with external holder " + this.lockHolder.ToString());
                    }

                    return false;
                }

                if (this.lockHolder == null)
                {
                    return false;
                }

                Process process;
                if (ProcessHelper.TryGetProcess(this.lockHolder.PID, out process) &&
                    string.Equals(this.lockHolder.OriginalCommand, ProcessHelper.GetCommandLine(process)))
                {
                    return true;
                }

                this.ClearHolder();
                return false;
            }
        }
    }
}