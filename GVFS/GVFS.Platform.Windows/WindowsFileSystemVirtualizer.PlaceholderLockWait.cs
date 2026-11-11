using GVFS.Common.Tracing;
using GVFS.Common.NamedPipes;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystemVirtualizer
    {
        private HResult DeferPlaceholderCreation(int commandId, string virtualPath, uint processId, string processName)
        {
            if (!this.TryRegisterCommand(commandId, out CancellationTokenSource cancellation))
            {
                cancellation.Dispose();
                this.Context.Tracer.RelatedWarning(this.CreateEventMetadata(virtualPath), "Cannot register deferred placeholder command");
                return HResult.InternalError;
            }

            if (!this.FileSystemCallbacks.GitIndexProjection.IsPathProjected(virtualPath, out string fileName, out bool isFolder))
            {
                this.TryCompleteCommand(commandId, HResult.FileNotFound);
                cancellation.Dispose();
                return HResult.Pending;
            }

            NamedPipeMessages.LockData holder = this.Context.Repository.GVFSLock.GetLockHolderForCallbacks(out long? ownerStartTime);
            if (isFolder || this.IsSpecialGitFile(fileName) || CanCreatePlaceholderForCommand(holder?.ParsedCommand))
            {
                FileOrNetworkRequest work = new FileOrNetworkRequest(
                    connection => this.GetPlaceholderInformationAsyncHandler(
                        cancellation.Token, connection, commandId, virtualPath, processId, processName),
                    () => cancellation.Dispose());
                if (!this.TryScheduleFileOrNetworkRequest(work, out Exception error))
                {
                    this.Context.Tracer.RelatedWarning(this.CreateEventMetadata(virtualPath, error), "Cannot schedule placeholder command");
                    this.TryCompleteCommand(commandId, (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    cancellation.Dispose();
                }

                return HResult.Pending;
            }

            PlaceholderCreationWaitQueue.Request request = new PlaceholderCreationWaitQueue.Request(
                commandId, virtualPath, processId, processName, cancellation);
            request.BlockingCommand = holder.ParsedCommand;
            request.BlockingReason = "GitCommand";
            if (this.ShouldRejectDependentRequest(request, holder, ownerStartTime))
            {
                request.Result = HResult.FileNotFound;
                this.FileSystemCallbacks.OnPlaceholderCreateBlockedForGit();
                this.TracePlaceholderWait(request, request.BlockingReason);
                this.TryCompleteCommand(commandId, HResult.FileNotFound);
                cancellation.Dispose();
            }
            else if (!this.placeholderWaitQueue.TryEnqueue(request, out bool stopping))
            {
                if (!stopping)
                {
                    Interlocked.Increment(ref this.placeholderWaitQueueFull);
                }

                request.Result = stopping ?
                    (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady :
                    (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable;
                this.TracePlaceholderWait(request, stopping ? "Stopping" : "QueueFull");
                this.TryCompleteCommand(commandId, request.Result.Value);
                cancellation.Dispose();
            }
            else
            {
                Interlocked.Increment(ref this.placeholderWaitStarted);
                this.TracePlaceholderWait(request, "Queued");
            }

            return HResult.Pending;
        }

        private bool ShouldRejectDependentRequest(PlaceholderCreationWaitQueue.Request request, NamedPipeMessages.LockData holder, long? ownerStartTime)
        {
            if (ReferenceEquals(request.CheckedHolder, holder))
            {
                return false;
            }

            request.CheckedHolder = holder;
            bool? related = request.ProcessId == holder.PID ? true : this.IsRequestDescendant(request.ProcessId, holder.PID, ownerStartTime);
            if (related == false)
            {
                return false;
            }

            request.BlockingCommand = holder.ParsedCommand;
            request.BlockingReason = related == true ? "OwnerOrDescendantRejected" : "DependencyUnknown";
            return true;
        }

        private void TracePlaceholderWait(PlaceholderCreationWaitQueue.Request request, string outcome)
        {
            EventMetadata metadata = this.CreateEventMetadata(request.Path);
            metadata.Add("commandId", request.CommandId);
            metadata.Add("triggeringProcessId", request.ProcessId);
            metadata.Add("triggeringProcessImageFileName", request.ProcessName);
            metadata.Add("outcome", outcome);
            metadata.Add("elapsedMs", Environment.TickCount64 - request.StartTick);
            metadata.Add("lockedGitCommand", request.BlockingCommand);
            metadata.Add("blockingReason", request.BlockingReason);
            metadata.Add("attempts", request.Attempts);
            metadata.Add("completionResult", request.Result?.ToString("X"));
            EventLevel level = outcome == "OwnerOrDescendantRejected" || outcome == "DependencyUnknown" ?
                EventLevel.Verbose : EventLevel.Informational;
            this.Context.Tracer.RelatedEvent(level, "PlaceholderLockWait", metadata);
        }

        private sealed class PlaceholderCreationWaitQueue : IDisposable
        {
            private const int MaximumRequests = 128;
            private const int WaitTimeoutMs = 2000;
            private const int PollIntervalMs = 25;

            private readonly WindowsFileSystemVirtualizer owner;
            private readonly object gate = new object();
            private readonly Dictionary<int, Request> requests = new Dictionary<int, Request>();
            private readonly AutoResetEvent wake = new AutoResetEvent(false);
            private readonly Thread thread;
            private bool stopping;
            private bool disposed;

            public PlaceholderCreationWaitQueue(WindowsFileSystemVirtualizer owner)
            {
                this.owner = owner;
                this.thread = new Thread(this.Run) { IsBackground = true, Name = "GVFS placeholder lock wait" };
                this.thread.Start();
            }

            public bool TryEnqueue(Request request, out bool stopping)
            {
                lock (this.gate)
                {
                    stopping = this.stopping;
                    if (stopping || this.requests.Count >= MaximumRequests)
                    {
                        return false;
                    }

                    this.requests.Add(request.CommandId, request);
                    this.wake.Set();
                    return true;
                }
            }

            public void Stop()
            {
                lock (this.gate)
                {
                    if (this.disposed)
                    {
                        return;
                    }

                    this.stopping = true;
                    this.wake.Set();
                }

                this.thread.Join();
            }

            public void Dispose()
            {
                this.Stop();
                lock (this.gate)
                {
                    if (!this.disposed)
                    {
                        this.disposed = true;
                        this.wake.Dispose();
                    }
                }
            }

            private void Run()
            {
                try
                {
                    while (true)
                    {
                        this.wake.WaitOne(PollIntervalMs);
                        List<Request> snapshot;
                        bool stop;
                        lock (this.gate)
                        {
                            snapshot = new List<Request>(this.requests.Values);
                            stop = this.stopping;
                        }

                        bool ready = false;
                        bool commandAllowsCreation = false;
                        NamedPipeMessages.LockData holder = null;
                        long? ownerStartTime = null;
                        if (!stop && snapshot.Count > 0)
                        {
                            holder = this.owner.Context.Repository.GVFSLock.GetLockHolderForCallbacks(out ownerStartTime);
                            commandAllowsCreation = CanCreatePlaceholderForCommand(holder?.ParsedCommand);
                            ready = commandAllowsCreation &&
                                this.owner.FileSystemCallbacks.GitIndexProjection.IsProjectionParseComplete();
                        }

                        foreach (Request request in snapshot)
                        {
                            bool schedule = false;
                            string outcome = null;
                            bool rejectDependent = !stop && !request.Scheduled && !request.Token.IsCancellationRequested &&
                                !commandAllowsCreation && this.owner.ShouldRejectDependentRequest(request, holder, ownerStartTime);
                            lock (this.gate)
                            {
                                if (!this.requests.TryGetValue(request.CommandId, out Request active) ||
                                    !ReferenceEquals(active, request) || request.Terminated)
                                {
                                    continue;
                                }

                                if (stop || request.Token.IsCancellationRequested)
                                {
                                    request.Terminated = true;
                                    if (!request.Scheduled)
                                    {
                                        this.requests.Remove(request.CommandId);
                                    }

                                    outcome = stop ? "Stopping" : "Cancelled";
                                    request.Result = (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                                    request.Cancellation.Cancel();
                                }
                                else if (!request.Scheduled)
                                {
                                    if (rejectDependent)
                                    {
                                        this.requests.Remove(request.CommandId);
                                        request.Terminated = true;
                                        request.Result = HResult.FileNotFound;
                                        outcome = request.BlockingReason;
                                    }
                                    else
                                    {
                                        if (!ready)
                                        {
                                            request.BlockingReason = commandAllowsCreation ? "ProjectionUnavailable" : "GitCommand";
                                            request.BlockingCommand = holder?.ParsedCommand;
                                        }

                                        long waited = Environment.TickCount64 - request.WaitTick;
                                        if (request.WaitedMs + waited >= WaitTimeoutMs)
                                        {
                                            this.requests.Remove(request.CommandId);
                                            request.Terminated = true;
                                            outcome = "TimedOut";
                                            request.Result = (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable;
                                            Interlocked.Increment(ref this.owner.placeholderWaitTimedOut);
                                        }
                                        else if (ready)
                                        {
                                            request.WaitedMs += waited;
                                            ++request.Attempts;
                                            request.Scheduled = true;
                                            schedule = true;
                                        }
                                    }
                                }
                            }

                            if (outcome != null)
                            {
                                this.owner.TracePlaceholderWait(request, outcome);
                                if (outcome != "Cancelled")
                                {
                                    if (rejectDependent)
                                    {
                                        this.owner.FileSystemCallbacks.OnPlaceholderCreateBlockedForGit();
                                    }

                                    this.owner.TryCompleteCommand(
                                        request.CommandId,
                                        rejectDependent ? HResult.FileNotFound :
                                        stop ? (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady :
                                            (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                                }

                                if (!request.Scheduled)
                                {
                                    request.Cancellation.Dispose();
                                }
                            }
                            else if (schedule)
                            {
                                this.Schedule(request);
                            }
                        }

                        if (stop)
                        {
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    this.owner.LogUnhandledExceptionAndExit(nameof(PlaceholderCreationWaitQueue), this.owner.CreateEventMetadata(exception: e));
                }
            }

            private void Schedule(Request request)
            {
                bool completed = false;
                FileOrNetworkRequest work = new FileOrNetworkRequest(
                    connection =>
                    {
                        completed = this.owner.GetPlaceholderInformationAsyncHandler(
                            request.Token, connection, request.CommandId, request.Path, request.ProcessId, request.ProcessName, request);
                    },
                    () => this.FinishAttempt(request, completed));
                if (!this.owner.TryScheduleFileOrNetworkRequest(work, out Exception error))
                {
                    this.owner.Context.Tracer.RelatedWarning(this.owner.CreateEventMetadata(request.Path, error), "Cannot schedule deferred placeholder command");
                    request.Result = (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                    this.owner.TryCompleteCommand(request.CommandId, (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    this.FinishAttempt(request, completed: true, outcome: "ScheduleFailed");
                }
            }

            private void FinishAttempt(Request request, bool completed, string outcome = "Completed")
            {
                bool trace;
                bool completeStopping;
                lock (this.gate)
                {
                    completeStopping = !completed && this.stopping && !request.Terminated;
                    trace = (completed || completeStopping) && !request.Terminated;
                    if (completed || this.stopping || request.Token.IsCancellationRequested)
                    {
                        if (completeStopping)
                        {
                            request.Result = (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                            request.Cancellation.Cancel();
                        }

                        request.Terminated = true;
                        this.requests.Remove(request.CommandId);
                        request.Cancellation.Dispose();
                    }
                    else
                    {
                        request.WaitTick = Environment.TickCount64;
                        request.Scheduled = false;
                    }
                }

                if (completeStopping)
                {
                    this.owner.TryCompleteCommand(request.CommandId, request.Result.Value);
                }

                if (trace)
                {
                    if (!request.Token.IsCancellationRequested && request.Result == HResult.Ok)
                    {
                        Interlocked.Increment(ref this.owner.placeholderWaitSucceeded);
                    }

                    this.owner.TracePlaceholderWait(request, completeStopping ? "Stopping" :
                        request.Token.IsCancellationRequested ? "Cancelled" : outcome);
                }
            }

            public sealed class Request
            {
                public Request(int commandId, string path, uint processId, string processName, CancellationTokenSource cancellation)
                {
                    this.CommandId = commandId;
                    this.Path = path;
                    this.ProcessId = processId;
                    this.ProcessName = processName;
                    this.Cancellation = cancellation;
                    this.Token = cancellation.Token;
                    this.StartTick = Environment.TickCount64;
                    this.WaitTick = this.StartTick;
                }

                public int CommandId { get; }
                public string Path { get; }
                public uint ProcessId { get; }
                public string ProcessName { get; }
                public CancellationTokenSource Cancellation { get; }
                public CancellationToken Token { get; }
                public long StartTick { get; }
                public long WaitTick { get; set; }
                public long WaitedMs { get; set; }
                public bool Scheduled { get; set; }
                public bool Terminated { get; set; }
                public NamedPipeMessages.LockData CheckedHolder { get; set; }
                public string BlockingCommand { get; set; }
                public string BlockingReason { get; set; }
                public int Attempts { get; set; }
                public HResult? Result { get; set; }
            }
        }
    }
}
