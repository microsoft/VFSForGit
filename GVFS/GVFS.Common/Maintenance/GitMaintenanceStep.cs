using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common.Maintenance
{
    public abstract class GitMaintenanceStep
    {
        public const string ObjectCacheLock = "git-maintenance-step.lock";
        private readonly object gitProcessLock = new object();

        public GitMaintenanceStep(GVFSContext context, GitObjects gitObjects, bool requireCacheLock = false)
        {
            this.Context = context;
            this.GitObjects = gitObjects;
            this.RequireCacheLock = requireCacheLock;
        }

        public abstract string TelemetryKey { get; }

        protected GVFSContext Context { get; }
        protected GitObjects GitObjects { get; }
        protected GitProcess GitProcess { get; private set; }
        protected bool Stopping { get; private set; }
        protected bool RequireCacheLock { get; }

        public static bool TryStopGitProcess(ITracer tracer, GitProcess process)
        {
            if (process == null)
            {
                return false;
            }

            if (process.TryKillRunningProcess())
            {
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "Killed background Git process during " + nameof(TryStopGitProcess),
                    metadata: null);
                return true;
            }
            else
            {
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "Failed to kill background Git process during " + nameof(TryStopGitProcess),
                    metadata: null);
                return false;
            }
        }

        public void Execute()
        {
            try
            {
                if (this.RequireCacheLock)
                {
                    using (FileBasedLock cacheLock = GVFSPlatform.Instance.CreateFileBasedLock(
                        this.Context.FileSystem,
                        this.Context.Tracer,
                        Path.Combine(this.Context.Enlistment.GitObjectsRoot, ObjectCacheLock)))
                    {
                        if (!cacheLock.TryAcquireLock())
                        {
                            this.Context.Tracer.RelatedInfo(this.TelemetryKey + ": Skipping work since another process holds the lock");
                            return;
                        }

                        this.CreateProcessAndRun();
                    }
                }
                else
                {
                    this.CreateProcessAndRun();
                }
            }
            catch (IOException e)
            {
                this.Context.Tracer.RelatedWarning(
                    metadata: this.CreateEventMetadata(e),
                    message: this.TelemetryKey + ": IOException while running action: " + e.Message,
                    keywords: Keywords.Telemetry);
            }
            catch (Exception e)
            {
                this.Context.Tracer.RelatedError(
                    metadata: this.CreateEventMetadata(e),
                    message: this.TelemetryKey + ": Exception while running action: " + e.Message,
                    keywords: Keywords.Telemetry);
                Environment.Exit((int)ReturnCode.GenericError);
            }
        }

        public void Stop()
        {
            lock (this.gitProcessLock)
            {
                this.Stopping = true;

                GitProcess process = this.GitProcess;

                if (process != null)
                {
                    if (process.TryKillRunningProcess())
                    {
                        this.Context.Tracer.RelatedEvent(
                            EventLevel.Informational,
                            this.TelemetryKey + ": killed background Git process during " + nameof(this.Stop),
                            metadata: null);
                    }
                    else
                    {
                        this.Context.Tracer.RelatedEvent(
                            EventLevel.Informational,
                            this.TelemetryKey + ": failed to kill background Git process during " + nameof(this.Stop),
                            metadata: null);
                    }
                }
            }
        }

        /// <summary>
        /// Implement this method to perform a maintenance step. If the object-cache lock is required
        /// (as specified by <see cref="RequireCacheLock"/>), then this step is not run unless we
        /// hold the lock.
        /// </summary>
        protected abstract void RunGitAction();
        
        protected GitProcess.Result RunGitCommand(Func<GitProcess, GitProcess.Result> work)
        {
            using (ITracer activity = this.Context.Tracer.StartActivity("RunGitCommand", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                if (this.Stopping)
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: null,
                        message: this.TelemetryKey + ": Not launching Git process because the mount is stopping",
                        keywords: Keywords.Telemetry);
                    return null;
                }

                GitProcess.Result result = work.Invoke(this.GitProcess);

                if (!this.Stopping && result?.ExitCodeIsFailure == true)
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: null,
                        message: this.TelemetryKey + ": Git process failed with errors:" + result.Errors,
                        keywords: Keywords.Telemetry);
                    return result;
                }

                return result;
            }
        }

        protected void LogWarning(string telemetryKey, string methodName, Exception exception)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", methodName);
            metadata.Add("ExceptionMessage", exception.Message);
            metadata.Add("StackTrace", exception.StackTrace);
            this.Context.Tracer.RelatedWarning(
                metadata: metadata,
                message: telemetryKey + ": Unexpected Exception while running a maintenance step: " + exception.Message,
                keywords: Keywords.Telemetry);
        }

        private void CreateProcessAndRun()
        {
            lock (this.gitProcessLock)
            {
                if (this.Stopping)
                {
                    return;
                }

                this.GitProcess = new GitProcess(this.Context.Enlistment);
            }

            this.RunGitAction();
        }

        private EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", this.TelemetryKey);

            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }
    }
}
