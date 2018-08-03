using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Threading;

namespace GVFS.Common.Prefetch
{
    public class BackgroundPrefetcher : IDisposable
    {
        private const string TelemetryKey = nameof(BackgroundPrefetcher);
        private readonly TimeSpan timerPeriod = TimeSpan.FromMinutes(15);
        private readonly TimeSpan timeBetweenPrefetches = TimeSpan.FromMinutes(70);

        private ITracer tracer;
        private GVFSEnlistment enlistment;
        private PhysicalFileSystem fileSystem;
        private GitObjects gitObjects;
        private Timer prefetchJobTimer;
        private Thread prefetchJobThread;

        public BackgroundPrefetcher(ITracer tracer, GVFSEnlistment enlistment, PhysicalFileSystem fileSystem, GitObjects gitObjects)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.fileSystem = fileSystem;
            this.gitObjects = gitObjects;

            this.prefetchJobThread = null;

            this.prefetchJobTimer = new Timer((state) => this.LaunchPrefetchJobIfIdle(), null, this.timerPeriod, this.timerPeriod);
        }

        public void Dispose()
        {
            this.prefetchJobTimer?.Dispose();
            this.prefetchJobTimer = null;
        }

        public bool LaunchPrefetchJobIfIdle()
        {
            if (this.prefetchJobThread?.IsAlive == true)
            {
                this.tracer.RelatedInfo(nameof(BackgroundPrefetcher) + ": background thread not idle, skipping timed start");
            }
            else
            {
                this.prefetchJobThread = new Thread(() => this.BackgroundPrefetch());
                this.prefetchJobThread.IsBackground = true;
                this.prefetchJobThread.Start();
                return true;
            }

            return false;
        }

        /// <summary>
        /// This method is used for test purposes only.
        /// </summary>
        public void WaitForPrefetchToFinish()
        {
            this.prefetchJobThread?.Join();
        }

        private void BackgroundPrefetch()
        {
            try
            {
                using (ITracer activity = this.tracer.StartActivity(nameof(this.BackgroundPrefetch), EventLevel.Informational))
                {
                    long last;
                    string error;

                    if (!CommitPrefetcher.TryGetMaxGoodPrefetchTimestamp(activity, this.enlistment, this.fileSystem, this.gitObjects, out last, out error))
                    {
                        activity.RelatedError(error);
                        return;
                    }

                    DateTime lastDateTime = EpochConverter.FromUnixEpochSeconds(last);
                    DateTime now = DateTime.UtcNow;

                    if (now <= lastDateTime + this.timeBetweenPrefetches)
                    {
                        activity.RelatedInfo(TelemetryKey + ": Skipping prefetch since most-recent prefetch ({0}) is too close to now ({1})", lastDateTime, now);
                        return;
                    }

                    if (!CommitPrefetcher.TryPrefetchCommitsAndTrees(activity, this.enlistment, this.fileSystem, this.gitObjects, out error))
                    {
                        activity.RelatedError($"{TelemetryKey}: {nameof(CommitPrefetcher.TryPrefetchCommitsAndTrees)} failed with error '{error}'");
                    }
                }
            }
            catch (ThreadAbortException)
            {
                this.tracer.RelatedInfo(TelemetryKey + ": Aborting prefetch background thread due to ThreadAbortException");
                return;
            }
            catch (IOException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.BackgroundPrefetch));
                metadata.Add("ExceptionMessage", e.Message);
                metadata.Add("StackTrace", e.StackTrace);
                this.tracer.RelatedWarning(
                    metadata: metadata,
                    message: TelemetryKey + ": IOException while running prefetch background thread (non-fatal): " + e.Message,
                    keywords: Keywords.Telemetry);
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.BackgroundPrefetch));
                metadata.Add("ExceptionMessage", e.Message);
                metadata.Add("StackTrace", e.StackTrace);
                this.tracer.RelatedError(
                    metadata: metadata,
                    message: TelemetryKey + ": Unexpected Exception while running prefetch background thread (fatal): " + e.Message,
                    keywords: Keywords.Telemetry);
                Environment.Exit((int)ReturnCode.GenericError);
            }
        }
    }
}
