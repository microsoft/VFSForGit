using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Virtualization.Background
{
    public class BackgroundFileSystemTaskRunner : IDisposable
    {
        private const int ActionRetryDelayMS = 50;
        private const int RetryFailuresLogThreshold = 200;
        private const int LogUpdateTaskThreshold = 25000;
        private static readonly string EtwArea = nameof(BackgroundFileSystemTaskRunner);

        private FileSystemTaskQueue backgroundTasks;
        private AutoResetEvent wakeUpThread;
        private Task backgroundThread;
        private bool isStopping;

        private GVFSContext context;

        // TODO 656051: Replace these callbacks with an interface
        private Func<FileSystemTaskResult> preCallback;
        private Func<FileSystemTask, FileSystemTaskResult> callback;
        private Func<FileSystemTaskResult> postCallback;

        public BackgroundFileSystemTaskRunner(
            GVFSContext context,
            Func<FileSystemTaskResult> preCallback,
            Func<FileSystemTask, FileSystemTaskResult> callback,
            Func<FileSystemTaskResult> postCallback,
            string databasePath)
        {
            this.context = context;
            this.preCallback = preCallback;
            this.callback = callback;
            this.postCallback = postCallback;

            string error;
            if (!FileSystemTaskQueue.TryCreate(
                this.context.Tracer,
                databasePath,
                new PhysicalFileSystem(),
                out this.backgroundTasks,
                out error))
            {
                string message = "Failed to create new background tasks folder: " + error;
                context.Tracer.RelatedError(message);
                throw new InvalidRepoException(message);
            }

            this.wakeUpThread = new AutoResetEvent(true);
        }

        // For Unit Testing
        protected BackgroundFileSystemTaskRunner()
        {
        }

        private enum AcquireGVFSLockResult
        {
            LockAcquired,
            ShuttingDown
        }

        public virtual bool IsEmpty
        {
            get { return this.backgroundTasks.IsEmpty; }
        }

        /// <summary>
        /// Gets the count of tasks in the background queue
        /// </summary>
        /// <remarks>
        /// This is an expensive call on .net core and you should avoid calling in performance critical paths.
        /// Use the IsEmpty property when checking if the queue has any items instead of Count.
        /// </remarks>
        public virtual int Count
        {
            get { return this.backgroundTasks.Count; }
        }

        public virtual void SetCallbacks(
            Func<FileSystemTaskResult> preCallback,
            Func<FileSystemTask, FileSystemTaskResult> callback,
            Func<FileSystemTaskResult> postCallback)
        {
            throw new NotSupportedException("This method is only meant for unit tests, and must be implemented by test class if necessary for use in tests");
        }

        public virtual void Start()
        {
            this.backgroundThread = Task.Factory.StartNew((Action)this.ProcessBackgroundTasks, TaskCreationOptions.LongRunning);
            if (!this.backgroundTasks.IsEmpty)
            {
                this.wakeUpThread.Set();
            }
        }

        public virtual void Enqueue(FileSystemTask backgroundTask)
        {
            this.backgroundTasks.EnqueueAndFlush(backgroundTask);

            if (!this.isStopping)
            {
                this.wakeUpThread.Set();
            }
        }

        public virtual void Shutdown()
        {
            this.isStopping = true;
            this.wakeUpThread.Set();
            this.backgroundThread.Wait();
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
                if (this.backgroundThread != null)
                {
                    this.backgroundThread.Dispose();
                    this.backgroundThread = null;
                }
            }
        }

        private AcquireGVFSLockResult WaitToAcquireGVFSLock()
        {
            int attempts = 0;
            while (!this.context.Repository.GVFSLock.TryAcquireLockForGVFS())
            {
                if (this.isStopping)
                {
                    return AcquireGVFSLockResult.ShuttingDown;
                }

                ++attempts;
                if (attempts > RetryFailuresLogThreshold)
                {
                    this.context.Tracer.RelatedWarning($"{nameof(this.WaitToAcquireGVFSLock)}: {nameof(BackgroundFileSystemTaskRunner)} unable to acquire lock, retrying");
                    attempts = 0;
                }

                Thread.Sleep(ActionRetryDelayMS);
            }

            return AcquireGVFSLockResult.LockAcquired;
        }

        private void ProcessBackgroundTasks()
        {
            FileSystemTask backgroundTask;

            while (true)
            {
                AcquireGVFSLockResult acquireLockResult = AcquireGVFSLockResult.ShuttingDown;

                try
                {
                    this.wakeUpThread.WaitOne();

                    if (this.isStopping)
                    {
                        return;
                    }

                    acquireLockResult = this.WaitToAcquireGVFSLock();
                    switch (acquireLockResult)
                    {
                        case AcquireGVFSLockResult.LockAcquired:
                            break;
                        case AcquireGVFSLockResult.ShuttingDown:
                            return;
                        default:
                            this.LogErrorAndExit("Invalid " + nameof(AcquireGVFSLockResult) + " result");
                            return;
                    }

                    this.RunCallbackUntilSuccess(this.preCallback, "PreCallback");

                    int tasksProcessed = 0;
                    while (this.backgroundTasks.TryPeek(out backgroundTask))
                    {
                        if (tasksProcessed % LogUpdateTaskThreshold == 0 &&
                            (tasksProcessed >= LogUpdateTaskThreshold || this.backgroundTasks.Count >= LogUpdateTaskThreshold))
                        {
                            this.LogTaskProcessingStatus(tasksProcessed);
                        }

                        if (this.isStopping)
                        {
                            // If we are stopping, then ProjFS has already been shut down
                            // Some of the queued background tasks may require ProjFS, and so it is unsafe to
                            // proceed.  GVFS will resume any queued tasks next time it is mounted
                            return;
                        }

                        FileSystemTaskResult callbackResult = this.callback(backgroundTask);
                        switch (callbackResult)
                        {
                            case FileSystemTaskResult.Success:
                                this.backgroundTasks.DequeueAndFlush(backgroundTask);
                                ++tasksProcessed;
                                break;

                            case FileSystemTaskResult.RetryableError:
                                if (!this.isStopping)
                                {
                                    Thread.Sleep(ActionRetryDelayMS);
                                }

                                break;

                            case FileSystemTaskResult.FatalError:
                                this.LogErrorAndExit("Callback encountered fatal error, exiting process");
                                break;

                            default:
                                this.LogErrorAndExit("Invalid background operation result");
                                break;
                        }
                    }

                    if (tasksProcessed >= LogUpdateTaskThreshold)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add("TasksProcessed", tasksProcessed);
                        metadata.Add(TracingConstants.MessageKey.InfoMessage, "Processing background tasks complete");
                        this.context.Tracer.RelatedEvent(EventLevel.Informational, "TaskProcessingStatus", metadata);
                    }

                    if (this.isStopping)
                    {
                        return;
                    }
                }
                catch (Exception e)
                {
                    this.LogErrorAndExit($"{nameof(this.ProcessBackgroundTasks)} caught unhandled exception, exiting process", e);
                }
                finally
                {
                    this.PerformPostTaskProcessing(acquireLockResult);
                }
            }
        }

        private void PerformPostTaskProcessing(AcquireGVFSLockResult acquireLockResult)
        {
            try
            {
                if (acquireLockResult == AcquireGVFSLockResult.LockAcquired)
                {
                    this.RunCallbackUntilSuccess(this.postCallback, "PostCallback");
                    if (this.backgroundTasks.IsEmpty)
                    {
                        this.context.Repository.GVFSLock.ReleaseLockHeldByGVFS();
                    }
                }
            }
            catch (Exception e)
            {
                this.LogErrorAndExit($"{nameof(this.ProcessBackgroundTasks)} caught unhandled exception in {nameof(this.PerformPostTaskProcessing)}, exiting process", e);
            }
        }

        private void RunCallbackUntilSuccess(Func<FileSystemTaskResult> callback, string errorHeader)
        {
            int attempts = 0;
            while (true)
            {
                FileSystemTaskResult callbackResult = callback();
                switch (callbackResult)
                {
                    case FileSystemTaskResult.Success:
                        return;

                    case FileSystemTaskResult.RetryableError:
                        if (this.isStopping)
                        {
                            return;
                        }

                        ++attempts;
                        if (attempts > RetryFailuresLogThreshold)
                        {
                            this.context.Tracer.RelatedWarning("RunCallbackUntilSuccess(" + errorHeader + "): callback failed, retrying");
                            attempts = 0;
                        }

                        Thread.Sleep(ActionRetryDelayMS);
                        break;

                    case FileSystemTaskResult.FatalError:
                        this.LogErrorAndExit(errorHeader + " encountered fatal error, exiting process");
                        return;

                    default:
                        this.LogErrorAndExit(errorHeader + " result could not be found");
                        return;
                }
            }
        }

        private void LogErrorAndExit(string message, Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            this.context.Tracer.RelatedError(metadata, message);
            Environment.Exit(1);
        }

        private void LogTaskProcessingStatus(int tasksProcessed)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("BackgroundOperations", EtwArea);
            metadata.Add("TasksProcessed", tasksProcessed);
            metadata.Add("TasksRemaining", this.backgroundTasks.Count);
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "TaskProcessingStatus", metadata);
        }
    }
}
