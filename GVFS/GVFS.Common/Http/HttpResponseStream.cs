#nullable enable

using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Http
{
    internal sealed class HttpResponseStream : Stream
    {
        private const int FatalErrorExitCode = 1;
        internal const int DefaultProgressIntervalSeconds = 30;
        private const string ProgressEventName = "NetworkBodyProgress";
        private const string TimeoutEventName = "NetworkBodyReadTimeout";
        private const string TransferEventName = "NetworkBodyTransfer";

        private readonly Stream _innerStream;
        private readonly TimeSpan _readTimeout;
        private readonly CancellationToken _requestCancellationToken;
        private readonly ITracer? _tracer;
        private readonly EventMetadata _requestMetadata;
        private readonly TimeSpan _progressInterval;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly object _gate = new object();

        private CancellationTokenSource? _pendingRead;
        private Timer? _progressTimer;
        private long _readStarted;
        private long _bytesRead;
        private long _previousBytesRead;
        private long _sampleSequence;
        private double _readMilliseconds;
        private double _maxReadMilliseconds;
        private double _lastProgressMilliseconds;
        private bool _reportedProgress;
        private int _reportingProgress;
        private volatile bool _disposed;
        private TransferOutcome _outcome;

        public HttpResponseStream(
            Stream innerStream,
            TimeSpan readTimeout,
            CancellationToken requestCancellationToken,
            ITracer? tracer = null,
            EventMetadata? requestMetadata = null,
            TimeSpan? progressInterval = null)
        {
            ArgumentNullException.ThrowIfNull(innerStream);
            if (readTimeout != Timeout.InfiniteTimeSpan &&
                (readTimeout <= TimeSpan.Zero || readTimeout.TotalMilliseconds > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(readTimeout));
            }

            this._innerStream = innerStream;
            this._readTimeout = readTimeout;
            this._requestCancellationToken = requestCancellationToken;
            this._tracer = tracer;
            this._requestMetadata = requestMetadata == null ? new EventMetadata() : new EventMetadata(requestMetadata);
            this._progressInterval = progressInterval ?? TimeSpan.FromSeconds(DefaultProgressIntervalSeconds);
            if (this._progressInterval <= TimeSpan.Zero || this._progressInterval.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(progressInterval));
            }

            this.ResponseId = Guid.NewGuid().ToString("N");
        }

        public string ResponseId { get; }

        public bool ReadFailed { get; private set; }

        public override bool CanRead => this._innerStream.CanRead;

        public override bool CanSeek => this._innerStream.CanSeek;

        public override bool CanWrite => this._innerStream.CanWrite;

        public override long Length => this._innerStream.Length;

        public override long Position
        {
            get { return this._innerStream.Position; }
            set { this._innerStream.Position = value; }
        }

        private enum TransferOutcome
        {
            InProgress,
            Completed,
            TimedOut,
            Canceled,
            ReadError,
            Closed,
        }

        public override void Flush() => this._innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            // The protocol readers are synchronous. Use the cancellable transport read
            // because HTTP response streams have no synchronous read deadline.
            // Completion and deadline callbacks require managed thread-pool capacity.
            return this.ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this._requestCancellationToken.ThrowIfCancellationRequested();

            using (CancellationTokenSource readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                this._requestCancellationToken))
            {
                lock (this._gate)
                {
                    ObjectDisposedException.ThrowIf(this._disposed, this);
                    if (this._pendingRead != null)
                    {
                        throw new InvalidOperationException("Another response body read is already in progress.");
                    }

                    if (buffer.Length == 0)
                    {
                        return 0;
                    }

                    this._pendingRead = readCancellation;
                    this._readStarted = Stopwatch.GetTimestamp();
                    if (this._tracer != null && this._progressTimer == null && this._outcome == TransferOutcome.InProgress)
                    {
                        this._progressTimer = new Timer(this.ReportProgress, null, this._progressInterval, this._progressInterval);
                    }
                }

                TransferOutcome outcome = TransferOutcome.InProgress;
                Exception? readError = null;
                readCancellation.CancelAfter(this._readTimeout);
                try
                {
                    int bytesRead = await this._innerStream.ReadAsync(buffer, readCancellation.Token).ConfigureAwait(false);
                    lock (this._gate)
                    {
                        this._bytesRead += bytesRead;
                        if (bytesRead > 0)
                        {
                            this._lastProgressMilliseconds = this._elapsed.Elapsed.TotalMilliseconds;
                        }
                    }

                    if (bytesRead == 0)
                    {
                        outcome = TransferOutcome.Completed;
                    }

                    return bytesRead;
                }
                catch (OperationCanceledException e) when (
                    readCancellation.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested &&
                    !this._requestCancellationToken.IsCancellationRequested &&
                    !this._disposed)
                {
                    this.ReadFailed = true;
                    outcome = TransferOutcome.TimedOut;
                    readError = new IOException(
                        $"No response body data arrived within {this._readTimeout.TotalSeconds} seconds.",
                        e);
                    throw readError;
                }
                catch (OperationCanceledException e)
                {
                    outcome = TransferOutcome.Canceled;
                    readError = e;
                    throw;
                }
                catch (Exception e)
                {
                    this.ReadFailed = true;
                    outcome = TransferOutcome.ReadError;
                    readError = e;
                    throw;
                }
                finally
                {
                    readCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                    EventMetadata? finalReport = null;
                    lock (this._gate)
                    {
                        double readMilliseconds = Stopwatch.GetElapsedTime(this._readStarted).TotalMilliseconds;
                        this._readMilliseconds += readMilliseconds;
                        this._maxReadMilliseconds = Math.Max(this._maxReadMilliseconds, readMilliseconds);
                        this._pendingRead = null;
                        if (outcome != TransferOutcome.InProgress)
                        {
                            finalReport = this.Finish(outcome, readError);
                        }
                    }

                    this.Trace(finalReport, outcome);
                }
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => this._innerStream.Seek(offset, origin);

        public override void SetLength(long value) => this._innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => this._innerStream.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    EventMetadata? finalReport = null;
                    bool disposeInner = false;
                    try
                    {
                        lock (this._gate)
                        {
                            if (this._disposed)
                            {
                                return;
                            }

                            disposeInner = true;
                            this._disposed = true;
                            finalReport = this.Finish(TransferOutcome.Closed);
                            Timer? progressTimer = this._progressTimer;
                            this._progressTimer = null;
                            try
                            {
                                this._pendingRead?.Cancel();
                            }
                            finally
                            {
                                progressTimer?.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        if (disposeInner)
                        {
                            try
                            {
                                this._innerStream.Dispose();
                            }
                            finally
                            {
                                this.Trace(finalReport, TransferOutcome.Closed);
                            }
                        }
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        private void ReportProgress(object? state)
        {
            if (Interlocked.CompareExchange(ref this._reportingProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                EventMetadata? report = null;
                lock (this._gate)
                {
                    if (!this._disposed && this._outcome == TransferOutcome.InProgress)
                    {
                        this._reportedProgress = true;
                        report = this.CreateReport();
                    }
                }

                this.Trace(report, TransferOutcome.InProgress);
            }
            catch (ObjectDisposedException) when (this._disposed)
            {
                // A captured progress sample can outlive response and tracer teardown.
                return;
            }
            catch (Exception e)
            {
                try
                {
                    this._tracer?.RelatedError(
                        new EventMetadata { { "Exception", e.ToString() } },
                        "The response body progress report failed.");
                }
                finally
                {
                    Environment.Exit(FatalErrorExitCode);
                }
            }
            finally
            {
                Volatile.Write(ref this._reportingProgress, 0);
            }
        }

        private EventMetadata? Finish(TransferOutcome outcome, Exception? readError = null)
        {
            if (this._outcome != TransferOutcome.InProgress)
            {
                return null;
            }

            this._outcome = outcome;
            this._progressTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (this._reportedProgress ||
                this._elapsed.Elapsed >= this._progressInterval ||
                outcome == TransferOutcome.TimedOut ||
                outcome == TransferOutcome.ReadError ||
                outcome == TransferOutcome.Canceled ||
                (outcome == TransferOutcome.Closed && this._pendingRead != null))
            {
                return this.CreateReport(readError);
            }

            return null;
        }

        private void Trace(EventMetadata? report, TransferOutcome outcome)
        {
            if (report == null || this._tracer == null)
            {
                return;
            }

            this._tracer.RelatedEvent(
                outcome == TransferOutcome.TimedOut || outcome == TransferOutcome.ReadError
                    ? EventLevel.Warning
                    : EventLevel.Informational,
                outcome == TransferOutcome.InProgress
                    ? ProgressEventName
                    : outcome == TransferOutcome.TimedOut ? TimeoutEventName : TransferEventName,
                report,
                Keywords.Network | Keywords.Telemetry);
        }

        private EventMetadata CreateReport(Exception? readError = null)
        {
            double currentReadMilliseconds = this._pendingRead == null
                ? 0
                : Stopwatch.GetElapsedTime(this._readStarted).TotalMilliseconds;
            EventMetadata metadata = new EventMetadata(this._requestMetadata)
            {
                { "ResponseId", this.ResponseId },
                { "SampleSequence", ++this._sampleSequence },
                { "BytesRead", this._bytesRead },
                { "BytesSinceLastProgress", this._bytesRead - this._previousBytesRead },
                { "ElapsedMs", this._elapsed.Elapsed.TotalMilliseconds },
                { "ReadTimeMs", this._readMilliseconds + currentReadMilliseconds },
                { "MaxReadWaitMs", Math.Max(this._maxReadMilliseconds, currentReadMilliseconds) },
                { "CurrentReadWaitMs", currentReadMilliseconds },
                { "MillisecondsSinceLastByte", this._elapsed.Elapsed.TotalMilliseconds - this._lastProgressMilliseconds },
                { "ReadPending", this._pendingRead != null },
                { "ReadTimeoutMs", this._readTimeout.TotalMilliseconds },
                { "Outcome", this._outcome.ToString() },
            };
            if (readError != null)
            {
                metadata.Add("Exception", readError.ToString());
            }

            this._previousBytesRead = this._bytesRead;
            return metadata;
        }
    }
}
