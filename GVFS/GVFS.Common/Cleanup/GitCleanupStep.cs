using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common.Cleanup
{
    public abstract class GitCleanupStep
    {
        public const string ObjectCacheLock = "git-cleanup-step.lock";
        private readonly object gitProcessLock = new object();

        public GitCleanupStep(GVFSContext context, GitObjects gitObjects)
        {
            this.Context = context;
            this.GitObjects = gitObjects;
        }

        public abstract string TelemetryKey { get; }

        protected GVFSContext Context { get; }
        protected GitObjects GitObjects { get; }
        protected GitProcess GitProcess { get; private set; }
        protected bool Stopping { get; private set; }

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
            this.Stopping = true;
            lock (this.gitProcessLock)
            {
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

        protected abstract void RunGitAction();

        protected bool HasNewerPrefetchPack(DateTime time)
        {
            foreach (DirectoryItemInfo info in this.Context.FileSystem.ItemsInDirectory(this.Context.Enlistment.GitPackRoot))
            {
                if (info.Name.EndsWith(".pack"))
                {
                    DateTime packTime = this.Context.FileSystem
                                                    .GetFileProperties(info.FullName)
                                                    .LastWriteTimeUTC;
                    if (packTime.CompareTo(time) > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

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

                if (!this.Stopping && result.HasErrors)
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
                message: telemetryKey + ": Unexpected Exception while running a cleanup step: " + exception.Message,
                keywords: Keywords.Telemetry);
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
