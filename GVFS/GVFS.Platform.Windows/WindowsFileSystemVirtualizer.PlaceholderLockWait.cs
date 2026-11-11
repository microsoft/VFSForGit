using GVFS.Common.Tracing;
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

            if (isFolder || this.IsSpecialGitFile(fileName))
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
            if (!this.placeholderWaitQueue.TryEnqueue(request))
            {
                this.TracePlaceholderWait(request, "QueueFull");
                this.TryCompleteCommand(commandId, (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                cancellation.Dispose();
            }
            else
            {
                this.TracePlaceholderWait(request, "Queued");
            }

            return HResult.Pending;
        }

        private void TracePlaceholderWait(PlaceholderCreationWaitQueue.Request request, string outcome)
        {
            EventMetadata metadata = this.CreateEventMetadata(request.Path);
            metadata.Add("commandId", request.CommandId);
            metadata.Add("triggeringProcessId", request.ProcessId);
            metadata.Add("triggeringProcessImageFileName", request.ProcessName);
            metadata.Add("outcome", outcome);
            metadata.Add("elapsedMs", Environment.TickCount64 - request.StartTick);
            metadata.Add("lockedGitCommand", this.Context.Repository.GVFSLock.GetLockedGitCommand());
            this.Context.Tracer.RelatedEvent(EventLevel.Informational, "PlaceholderLockWait", metadata);
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

            public bool TryEnqueue(Request request)
            {
                lock (this.gate)
                {
                    if (this.stopping || this.requests.Count >= MaximumRequests)
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
                        if (!stop && snapshot.Count > 0)
                        {
                            ready = this.owner.CanCreatePlaceholder() &&
                                this.owner.FileSystemCallbacks.GitIndexProjection.IsProjectionParseComplete();
                        }

                        foreach (Request request in snapshot)
                        {
                            bool schedule = false;
                            string outcome = null;
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
                                    request.Cancellation.Cancel();
                                }
                                else if (!request.Scheduled)
                                {
                                    if (Environment.TickCount64 - request.StartTick >= WaitTimeoutMs)
                                    {
                                        this.requests.Remove(request.CommandId);
                                        request.Terminated = true;
                                        outcome = "TimedOut";
                                    }
                                    else if (ready)
                                    {
                                        request.Scheduled = true;
                                        schedule = true;
                                    }
                                }
                            }

                            if (outcome != null)
                            {
                                this.owner.TracePlaceholderWait(request, outcome);
                                if (outcome != "Cancelled")
                                {
                                    this.owner.TryCompleteCommand(
                                        request.CommandId,
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
                    this.owner.TryCompleteCommand(request.CommandId, (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    this.FinishAttempt(request, completed: true);
                }
            }

            private void FinishAttempt(Request request, bool completed)
            {
                lock (this.gate)
                {
                    if (completed || this.stopping || request.Token.IsCancellationRequested)
                    {
                        this.requests.Remove(request.CommandId);
                        request.Cancellation.Dispose();
                    }
                    else
                    {
                        request.Scheduled = false;
                    }
                }

                if (completed)
                {
                    this.owner.TracePlaceholderWait(request, request.Token.IsCancellationRequested ? "Cancelled" : "Completed");
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
                }

                public int CommandId { get; }
                public string Path { get; }
                public uint ProcessId { get; }
                public string ProcessName { get; }
                public CancellationTokenSource Cancellation { get; }
                public CancellationToken Token { get; }
                public long StartTick { get; }
                public bool Scheduled { get; set; }
                public bool Terminated { get; set; }
            }
        }
    }
}
