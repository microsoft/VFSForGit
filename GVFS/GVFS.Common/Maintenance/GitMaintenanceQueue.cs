using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace GVFS.Common.Maintenance
{
    public class GitMaintenanceQueue
    {
        private GVFSContext context;
        private BlockingCollection<GitMaintenanceStep> queue = new BlockingCollection<GitMaintenanceStep>();
        private GitMaintenanceStep currentStep;

        public GitMaintenanceQueue(GVFSContext context)
        {
            this.context = context;
            Thread worker = new Thread(() => this.RunQueue());
            worker.Name = "MaintenanceWorker";
            worker.IsBackground = true;
            worker.Start();
        }

        public void Enqueue(GitMaintenanceStep step)
        {
            try
            {
                this.queue?.Add(step);
            }
            catch (InvalidOperationException)
            {
                // We called queue.CompleteAdding()
            }
        }

        public void Stop()
        {
            this.queue?.CompleteAdding();
            this.currentStep?.Stop();
        }

        /// <summary>
        /// This method is public for test purposes only.
        /// </summary>
        public bool EnlistmentRootReady()
        {
            // If a user locks their drive or disconnects an external drive while the mount process
            // is running, then it will appear as if the directories below do not exist or throw
            // a "Device is not ready" error.
            try
            {
                return this.context.FileSystem.DirectoryExists(this.context.Enlistment.EnlistmentRoot)
                         && this.context.FileSystem.DirectoryExists(this.context.Enlistment.GitObjectsRoot);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private void RunQueue()
        {
            while (true)
            {
                if (!this.queue.TryTake(out this.currentStep, Timeout.Infinite)
                    || this.queue.IsAddingCompleted)
                {
                    // A stop was requested
                    this.queue.Dispose();
                    this.queue = null;
                    return;
                }

                if (this.EnlistmentRootReady())
                {
                    try
                    {
                        this.currentStep.Execute();
                    }
                    catch (Exception e)
                    {
                        this.LogErrorAndExit(
                            area: nameof(GitMaintenanceQueue),
                            methodName: nameof(this.RunQueue),
                            exception: e);
                    }
                }
            }
        }

        private void LogError(string area, string methodName, Exception exception)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", methodName);
            metadata.Add("ExceptionMessage", exception.Message);
            metadata.Add("StackTrace", exception.StackTrace);
            this.context.Tracer.RelatedError(
                metadata: metadata,
                message: area + ": Unexpected Exception while running maintenance steps (fatal): " + exception.Message,
                keywords: Keywords.Telemetry);
        }

        private void LogErrorAndExit(string area, string methodName, Exception exception)
        {
            this.LogError(area, methodName, exception);
            Environment.Exit((int)ReturnCode.GenericError);
        }
    }
}
