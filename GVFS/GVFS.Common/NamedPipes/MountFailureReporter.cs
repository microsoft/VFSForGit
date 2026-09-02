using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// The result of a thread trying to take ownership of the mount-failure path.
    /// </summary>
    public enum MountFailureOwnership
    {
        /// <summary>
        /// The calling thread now owns reporting and must drive it to process exit.
        /// </summary>
        Owner,

        /// <summary>
        /// The calling thread already owned reporting and has re-entered, because
        /// something threw while it was reporting and an outer catch block called back
        /// in. It must not wait again: it would block forever on a signal that it is
        /// itself responsible for observing.
        /// </summary>
        Reentrant,

        /// <summary>
        /// Another thread owns reporting and will exit the process. The calling thread
        /// must not continue with a half-initialized mount.
        /// </summary>
        NotOwner,
    }

    /// <summary>
    /// Server half of the mount-failure handshake: keeps a failed mount process alive
    /// just long enough for a client to read the reason over the named pipe.
    /// </summary>
    /// <remarks>
    /// Without this, the mount process sets <c>MountFailed</c> and exits immediately.
    /// The client polls GetStatus every 100ms, so it usually loses the race, the pipe
    /// breaks mid-request, and the user sees a transport error instead of the cause.
    /// <para>
    /// The wait is deliberately short. Until the process exits it still holds the mount
    /// lock and the named pipe, so a longer wait delays the user's next mount attempt.
    /// </para>
    /// <para>
    /// This type owns only the handshake state. Process-exit policy stays with the
    /// caller. The client half of the same protocol is
    /// <see cref="GVFSEnlistment.WaitUntilMounted(ITracer, string, string, bool, out string, Action{string})"/>.
    /// </para>
    /// </remarks>
    public sealed class MountFailureReporter : IDisposable
    {
        /// <summary>
        /// Cap on how long to wait once a client is known to be polling.
        /// </summary>
        public const int DefaultReportTimeoutMs = 2000;

        /// <summary>
        /// Cap used when no client has received a status yet. It only has to cover the
        /// gap between the pipe opening and the client's first poll.
        /// </summary>
        public const int DefaultNoClientTimeoutMs = 500;

        /// <summary>
        /// Grace period after a client reads the failure. Any client can satisfy the
        /// wait, and it is not necessarily the one that is mounting -- a concurrent
        /// "gvfs status" polls the same pipe. Keeping this above the client's 100ms poll
        /// interval means the mounting client still gets its answer on its next poll.
        /// </summary>
        public const int DefaultDrainMs = 150;

        private readonly ITracer tracer;
        private readonly int reportTimeoutMs;
        private readonly int noClientTimeoutMs;
        private readonly int drainMs;
        private readonly ManualResetEvent failureReported;

        private volatile string failureMessage;
        private volatile bool namedPipeReady;
        private volatile bool clientReceivedStatus;

        // 0 means no thread owns the failure path yet.
        private int owningThreadId;

        public MountFailureReporter(ITracer tracer)
            : this(tracer, DefaultReportTimeoutMs, DefaultNoClientTimeoutMs, DefaultDrainMs)
        {
        }

        /// <summary>
        /// Test constructor. Lets unit tests use short timeouts so they do not pay the
        /// production waits.
        /// </summary>
        internal MountFailureReporter(ITracer tracer, int reportTimeoutMs, int noClientTimeoutMs, int drainMs)
        {
            this.tracer = tracer;
            this.reportTimeoutMs = reportTimeoutMs;
            this.noClientTimeoutMs = noClientTimeoutMs;
            this.drainMs = drainMs;
            this.failureReported = new ManualResetEvent(false);
        }

        /// <summary>
        /// The reason the mount failed, or null if the mount has not failed. Sent to the
        /// client in the GetStatus response.
        /// </summary>
        public string FailureMessage
        {
            get { return this.failureMessage; }
        }

        /// <summary>
        /// Formats a failure message without letting the formatting itself throw.
        /// </summary>
        /// <remarks>
        /// Callers pass messages built from external text (exception strings, server
        /// responses) that can contain unbalanced braces. A FormatException here would
        /// mask the failure being reported, so fall back to the unformatted string.
        /// </remarks>
        public static string FormatFailure(string error, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return error;
            }

            try
            {
                return string.Format(error, args);
            }
            catch (FormatException)
            {
                return error;
            }
        }

        /// <summary>
        /// Records that the named pipe server is accepting requests. Failures before this
        /// point cannot be reported over the pipe and must not wait for a reader.
        /// </summary>
        public void OnNamedPipeReady()
        {
            this.namedPipeReady = true;
        }

        /// <summary>
        /// Attempts to take ownership of the mount-failure path, publishing
        /// <paramref name="message"/> as the reason if this call wins.
        /// </summary>
        public MountFailureOwnership TryTakeOwnership(string message)
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            int previousOwner = Interlocked.CompareExchange(ref this.owningThreadId, currentThreadId, 0);

            if (previousOwner == currentThreadId)
            {
                return MountFailureOwnership.Reentrant;
            }

            if (previousOwner != 0)
            {
                return MountFailureOwnership.NotOwner;
            }

            this.failureMessage = message;
            return MountFailureOwnership.Owner;
        }

        /// <summary>
        /// Records that a GetStatus response reached a client, and releases
        /// <see cref="WaitForClientToReadFailure"/> when that response carried the
        /// failure.
        /// </summary>
        /// <param name="carriedMountFailure">
        /// True when the delivered response reported MountFailed.
        /// </param>
        public void OnStatusDelivered(bool carriedMountFailure)
        {
            // Only count a client that actually received a status. Recording this before
            // the send succeeds would let a client that connected and then disconnected
            // extend the post-failure wait.
            this.clientReceivedStatus = true;

            if (carriedMountFailure)
            {
                this.failureReported.Set();
            }
        }

        /// <summary>
        /// Blocks until a client has read the failure, or until a short timeout elapses.
        /// Returns immediately when no client could possibly read it.
        /// </summary>
        public void WaitForClientToReadFailure()
        {
            if (!this.namedPipeReady)
            {
                // No client can read the failure. The caller detects this case by
                // watching the mount process exit code instead.
                return;
            }

            int timeoutMs = this.clientReceivedStatus ? this.reportTimeoutMs : this.noClientTimeoutMs;

            if (this.failureReported.WaitOne(timeoutMs))
            {
                Thread.Sleep(this.drainMs);
            }
            else
            {
                this.tracer.RelatedWarning(
                    null,
                    $"{nameof(this.WaitForClientToReadFailure)}: No client read the mount failure within {timeoutMs}ms. Exiting anyway.",
                    Keywords.Telemetry);
            }
        }

        public void Dispose()
        {
            this.failureReported.Dispose();
        }
    }
}
