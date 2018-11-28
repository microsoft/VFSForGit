using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace GVFS.Common.Cleanup
{
    public class GitCleanupQueue
    {
        private GVFSContext context;
        private BlockingCollection<GitCleanupStep> queue = new BlockingCollection<GitCleanupStep>();
        private CancellationTokenSource cancellationToken = new CancellationTokenSource();
        private GitCleanupStep currentStep;

        public GitCleanupQueue(GVFSContext context)
        {
            this.context = context;
            Thread cleanupWorker = new Thread(() => this.RunQueue());
            cleanupWorker.Name = "CleanupWorker";
            cleanupWorker.Start();
        }

        public void Enqueue(GitCleanupStep step)
        {
            this.queue.Add(step);
        }

        public void Stop()
        {
            this.cancellationToken.Cancel();
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
                try
                {
                    this.queue.TryTake(out this.currentStep, Timeout.Infinite, this.cancellationToken.Token);
                }
                catch (OperationCanceledException)
                {
                    // Only gets thrown when stop is requested
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
                            telemetryKey: nameof(GitCleanupQueue),
                            methodName: nameof(this.RunQueue),
                            exception: e);
                    }                   
                }
            }
        }

        private void LogError(string telemetryKey, string methodName, Exception exception)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", methodName);
            metadata.Add("ExceptionMessage", exception.Message);
            metadata.Add("StackTrace", exception.StackTrace);
            this.context.Tracer.RelatedError(
                metadata: metadata,
                message: telemetryKey + ": Unexpected Exception while running cleanup steps (fatal): " + exception.Message,
                keywords: Keywords.Telemetry);
        }

        private void LogErrorAndExit(string telemetryKey, string methodName, Exception exception)
        {
            this.LogError(telemetryKey, methodName, exception);
            Environment.Exit((int)ReturnCode.GenericError);
        }
    }
}
