using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace GVFS.Common.Cleanup
{
    public class GitCleanupQueue
    {
        private readonly object threadLock = new object();
        private readonly object currentStepLock = new object();
        private GVFSContext context;
        private ConcurrentQueue<GitCleanupStep> queue;
        private Thread thread;
        private GitCleanupStep currentStep;
        private bool stopping;

        public GitCleanupQueue(GVFSContext context)
        {
            this.context = context;
            this.queue = new ConcurrentQueue<GitCleanupStep>();
        }

        public void Enqueue(GitCleanupStep step)
        {
            this.queue.Enqueue(step);

            lock (this.threadLock)
            {
                if (this.thread == null)
                {
                    this.thread = new Thread(() => this.RunQueue());
                    this.thread.IsBackground = true;

                    try
                    {
                        this.thread.Start();
                    }
                    catch (ThreadStateException e)
                    {
                        this.LogError(nameof(GitCleanupQueue), nameof(this.Enqueue), e);
                    }
                    catch (OutOfMemoryException e)
                    {
                        this.LogError(nameof(GitCleanupQueue), nameof(this.Enqueue), e);
                    }
                }
            }
        }

        public void Stop()
        {
            this.stopping = true;

            GitCleanupStep stepToStop;

            lock (this.currentStepLock)
            {
                stepToStop = this.currentStep;
            }

            if (stepToStop != null)
            {
                stepToStop.Stop();
            }
        }

        /// <summary>
        /// This method is used for test purposes only.
        /// </summary>
        public void WaitForStepsToFinish()
        {
            this.thread?.Join();
        }

        /// <summary>
        /// This method is used for test purposes only.
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
                lock (this.threadLock)
                {
                    if (this.queue.Count == 0
                        || !this.EnlistmentRootReady())
                    {
                        this.thread = null;
                        return;
                    }
                }

                lock (this.currentStepLock)
                {
                    this.queue.TryDequeue(out this.currentStep);
                }

                if (this.stopping || this.currentStep == null)
                {
                    this.thread = null;
                    return;
                }

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

                this.currentStep = null;
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
