using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RGFS.GVFlt
{
    public class ReliableBackgroundOperations : IDisposable
    {
        private const int ActionRetryDelayMS = 50;
        private const int RetryFailuresLogThreshold = 200;
        private const int MaxCallbackAttemptsOnShutdown = 5;
        private const int LogUpdateTaskThreshold = 25000;
        private static readonly string EtwArea = "ProcessBackgroundOperations";
        
        private BackgroundGitUpdateQueue backgroundOperations;
        private AutoResetEvent wakeUpThread;
        private Task backgroundThread;
        private bool isStopping;

        private RGFSContext context;

        // TODO 656051: Replace these callbacks with an interface
        private Func<CallbackResult> preCallback;
        private Func<GVFltCallbacks.BackgroundGitUpdate, CallbackResult> callback;
        private Func<CallbackResult> postCallback;

        public ReliableBackgroundOperations(
            RGFSContext context,
            Func<CallbackResult> preCallback,             
            Func<GVFltCallbacks.BackgroundGitUpdate, CallbackResult> callback,
            Func<CallbackResult> postCallback,
            string databasePath)
        {
            this.context = context;
            this.preCallback = preCallback;
            this.callback = callback;
            this.postCallback = postCallback;

            string error;
            if (!BackgroundGitUpdateQueue.TryCreate(
                this.context.Tracer,
                databasePath,
                new PhysicalFileSystem(),
                out this.backgroundOperations,
                out error))
            {
                string message = "Failed to create new background operations folder: " + error;
                context.Tracer.RelatedError(message);
                throw new InvalidRepoException(message);
            }

            this.wakeUpThread = new AutoResetEvent(true);
        }

        // For Unit Testing
        protected ReliableBackgroundOperations()
        {
        }

        private enum AcquireRGFSLockResult
        {
            LockAcquired,
            ShuttingDown
        }

        public virtual int Count
        {
            get { return this.backgroundOperations.Count; }
        }

        public virtual void Start()
        {            
            this.backgroundThread = Task.Factory.StartNew((Action)this.ProcessBackgroundOperations, TaskCreationOptions.LongRunning);
            if (this.backgroundOperations.Count > 0)
            {
                this.wakeUpThread.Set();
            }
        }

        public virtual void Enqueue(GVFltCallbacks.BackgroundGitUpdate backgroundOperation)
        {
            this.backgroundOperations.EnqueueAndFlush(backgroundOperation);

            if (!this.isStopping)
            {
                this.wakeUpThread.Set();
            }
        }

        public void Shutdown()
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
            if (this.backgroundThread != null)
            {
                this.backgroundThread.Dispose();
                this.backgroundThread = null;
            }
        }
                
        private AcquireRGFSLockResult WaitToAcquireRGFSLock()
        {
            int attempts = 0;
            while (!this.context.Repository.RGFSLock.TryAcquireLock())
            {
                if (this.isStopping)
                {
                    return AcquireRGFSLockResult.ShuttingDown;
                }

                ++attempts;
                if (attempts > RetryFailuresLogThreshold)
                {
                    this.context.Tracer.RelatedWarning("WaitToAcquireRGFSLock: TryAcquireLock unable to acquire lock, retrying");
                    attempts = 0;
                }

                Thread.Sleep(ActionRetryDelayMS);
            }

            return AcquireRGFSLockResult.LockAcquired;
        }
     
        private void ProcessBackgroundOperations()
        {
            GVFltCallbacks.BackgroundGitUpdate backgroundOperation;

            while (true)
            {
                AcquireRGFSLockResult acquireLockResult = AcquireRGFSLockResult.ShuttingDown;

                try
                {
                    this.wakeUpThread.WaitOne();

                    if (this.isStopping)
                    {
                        return;
                    }

                    acquireLockResult = this.WaitToAcquireRGFSLock();
                    switch (acquireLockResult)
                    {
                        case AcquireRGFSLockResult.LockAcquired:
                            break;
                        case AcquireRGFSLockResult.ShuttingDown:
                            return;
                        default:
                            this.LogErrorAndExit("Invalid " + nameof(AcquireRGFSLockResult) + " result");
                            return;
                    }

                    this.RunCallbackUntilSuccess(this.preCallback, "PreCallback");

                    int tasksProcessed = 0;
                    while (this.backgroundOperations.TryPeek(out backgroundOperation))
                    {
                        if (tasksProcessed % LogUpdateTaskThreshold == 0 && 
                            (tasksProcessed >= LogUpdateTaskThreshold || this.backgroundOperations.Count >= LogUpdateTaskThreshold))
                        {
                            this.LogTaskProcessingStatus(tasksProcessed);
                        }

                        if (this.isStopping)
                        {
                            // If we are stopping, then GVFlt has already been shut down
                            // Some of the queued background tasks may require GVFlt, and so it is unsafe to
                            // proceed.  RGFS will resume any queued tasks next time it is mounted
                            return;
                        }
                        
                        CallbackResult callbackResult = this.callback(backgroundOperation);
                        switch (callbackResult)
                        {
                            case CallbackResult.Success:                                
                                this.backgroundOperations.DequeueAndFlush(backgroundOperation);
                                ++tasksProcessed;
                                break;

                            case CallbackResult.RetryableError:
                                if (!this.isStopping)
                                {
                                    Thread.Sleep(ActionRetryDelayMS);
                                }

                                break;

                            case CallbackResult.FatalError:
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
                    this.LogErrorAndExit("ProcessBackgroundOperations caught unhandled exception, exiting process", e);
                }
                finally
                {
                    if (acquireLockResult == AcquireRGFSLockResult.LockAcquired)
                    {
                        this.RunCallbackUntilSuccess(this.postCallback, "PostCallback");
                        if (this.backgroundOperations.Count == 0)
                        {
                            this.context.Repository.RGFSLock.ReleaseLock();
                        }
                    }
                }
            }
        }

        private void RunCallbackUntilSuccess(Func<CallbackResult> callback, string errorHeader)
        {
            int attempts = 0;
            while (true)
            {
                CallbackResult callbackResult = callback();
                switch (callbackResult)
                {
                    case CallbackResult.Success:
                        return;

                    case CallbackResult.RetryableError:
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

                    case CallbackResult.FatalError:
                        this.LogErrorAndExit(errorHeader + " encountered fatal error, exiting process");
                        return;

                    default:
                        this.LogErrorAndExit(errorHeader + " result could not be found");
                        return;
                }
            }
        }

        private void LogWarning(string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            this.context.Tracer.RelatedWarning(metadata, message);
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
            metadata.Add("TasksRemaining", this.backgroundOperations.Count);
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "TaskProcessingStatus", metadata);
        }
    }
}
