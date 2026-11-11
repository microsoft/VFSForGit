using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Service
{
    public sealed class InstallationTelemetryMonitor : IDisposable
    {
        private readonly InstallationTelemetryStore store;
        private readonly ITracer tracer;
        private readonly Func<string> resolvePipe;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly ManualResetEventSlim wake = new ManualResetEventSlim();
        private readonly Lock lifecycleLock = new Lock();
        private Thread thread;
        private bool disposed;
        private bool hasPendingRecords;

        public InstallationTelemetryMonitor(InstallationTelemetryStore store, ITracer tracer, Func<string> resolvePipe)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(resolvePipe);
            this.store = store;
            this.tracer = tracer;
            this.resolvePipe = resolvePipe;
        }

        public void Start()
        {
            lock (this.lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                if (this.thread != null)
                {
                    throw new InvalidOperationException("The installation telemetry monitor is already started.");
                }

                this.thread = new Thread(this.Run) { IsBackground = true, Name = nameof(InstallationTelemetryMonitor) };
                this.thread.Start();
            }
        }

        public void Wake()
        {
            lock (this.lifecycleLock)
            {
                if (!this.disposed)
                {
                    this.wake.Set();
                }
            }
        }

        public async Task<int> DrainAsync(CancellationToken cancellationToken)
        {
            List<KeyValuePair<string, InstallationTelemetryRecord>> records = this.store.ReadBatch(cancellationToken);
            this.hasPendingRecords = records.Count > 0;
            if (records.Count == 0)
            {
                return 0;
            }

            string pipeName = this.resolvePipe();
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return 0;
            }

            if (pipeName.Length > 256 || pipeName.Equals("anonymous", StringComparison.OrdinalIgnoreCase)
                || pipeName.IndexOfAny(new char[] { '\\', '/', ':', '\r', '\n', '\0' }) >= 0)
            {
                throw new InvalidDataException("Telemetry pipe configuration contains an invalid pipe name.");
            }

            int sent = 0;
            foreach (KeyValuePair<string, InstallationTelemetryRecord> item in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TelemetryDaemonEventListener.PipeMessage message = new TelemetryDaemonEventListener.PipeMessage
                {
                    Version = ProcessHelper.GetCurrentProcessVersion(),
                    Architecture = ProcessHelper.GetCurrentProcessArchitecture(),
                    ProviderName = GVFSConstants.Service.ServiceName,
                    EventName = item.Value.EventType,
                    EventLevel = EventLevel.Informational,
                    EventOpcode = EventOpcode.Info,
                    Payload = new TelemetryDaemonEventListener.PipeMessage.PipeMessagePayload
                    {
                        Json = JsonSerializer.Serialize(item.Value, ServiceJsonContext.Default.InstallationTelemetryRecord),
                    },
                };
                await SendAsync(pipeName, message.ToJson(), cancellationToken).ConfigureAwait(false);

                this.store.Complete(item.Key);
                if (++sent == 16)
                {
                    break;
                }
            }

            return sent;
        }

        public static async Task SendAsync(string pipeName, string json, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
                if (pipe.TransmissionMode == PipeTransmissionMode.Byte)
                {
                    json += "\n";
                }

                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await pipe.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
                await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            lock (this.lifecycleLock)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                this.shutdown.Cancel();
                this.wake.Set();
            }

            this.thread?.Join();
            this.wake.Dispose();
            this.shutdown.Dispose();
        }

        private void Run()
        {
            int failures = 0;
            try
            {
                while (!this.shutdown.IsCancellationRequested)
                {
                    this.wake.Reset();
                    try
                    {
                        int sent = this.DrainAsync(this.shutdown.Token).GetAwaiter().GetResult();
                        failures = sent > 0 || !this.hasPendingRecords ? 0 : Math.Min(failures + 1, 3);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException
                        || ex is Win32Exception || ex is OperationCanceledException)
                    {
                        if (this.shutdown.IsCancellationRequested)
                        {
                            break;
                        }

                        this.store.Health.ReportFailure(InstallationTelemetryHealth.FailureReason.DeliveryFailed, ex.ToString());
                        failures = Math.Min(failures + 1, 3);
                    }

                    this.store.Health.Flush();
                    int interval = failures switch { 0 => 10_000, 1 => 30_000, 2 => 60_000, _ => 300_000 };
                    this.wake.Wait(interval, this.shutdown.Token);
                }
            }
            catch (OperationCanceledException) when (this.shutdown.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                this.tracer.RelatedEvent(EventLevel.Error, "InstallationTelemetryWorkerStopped", new EventMetadata { { "Exception", ex.ToString() } });
                this.store.Health.ReportFailure(InstallationTelemetryHealth.FailureReason.WorkerStopped, ex.ToString());
                Environment.Exit((int)ReturnCode.GenericError);
            }
        }
    }
}
