using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// Server half of the mount-failure handshake. A failing mount process stays alive
    /// until a client has read the reason over the named pipe, or until a short timeout
    /// elapses, so the failure response reaches the client before the process exits.
    /// </summary>
    /// <remarks>
    /// Until the process exits it holds the mount lock and the named pipe, so the wait is
    /// bounded and short. This type owns only the handshake state. The caller decides how
    /// and when the process exits. The client half of the protocol is
    /// <see cref="GVFSEnlistment.WaitUntilMounted(ITracer, string, string, bool, out string, Action{string})"/>.
    /// </remarks>
    public sealed class MountFailureReporter
    {
        /// <summary>
        /// Cap on how long to wait once a client is known to be polling.
        /// </summary>
        internal const int DefaultReportTimeoutMs = 2000;

        /// <summary>
        /// Cap used when no client has received a status yet. It only has to cover the
        /// gap between the pipe opening and the client's first poll.
        /// </summary>
        internal const int DefaultNoClientTimeoutMs = 500;

        /// <summary>
        /// Time to stay alive after a client reads the failure. Any client can satisfy the
        /// wait, and it is not necessarily the mounting one: a concurrent "gvfs status"
        /// polls the same pipe. The mounting client must get at least one more poll before
        /// the process exits, so this is longer than the client's poll interval.
        /// </summary>
        internal const int DefaultDrainMs = GVFSEnlistment.MountStatusPollIntervalMs * 3 / 2;

        private readonly ITracer tracer;
        private readonly IMountFailureEnvironment environment;

        // Held for the life of the process, which ends through Environment.Exit.
        private readonly ManualResetEvent failureReported = new ManualResetEvent(false);

        private volatile string failureMessage;
        private volatile bool namedPipeReady;
        private volatile bool clientReceivedStatus;

        // 0 means no thread owns the failure path yet.
        private int owningThreadId;

        public MountFailureReporter(ITracer tracer)
            : this(tracer, new ProcessMountFailureEnvironment())
        {
        }

        internal MountFailureReporter(ITracer tracer, IMountFailureEnvironment environment)
        {
            this.tracer = tracer;
            this.environment = environment;
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
        /// Records that the named pipe server is accepting requests. A failure before this
        /// point cannot be reported over the pipe, so it does not wait for a client.
        /// </summary>
        public void OnNamedPipeReady()
        {
            this.namedPipeReady = true;
        }

        /// <summary>
        /// Takes ownership of the mount-failure path.
        /// </summary>
        /// <returns>
        /// True if the calling thread now owns reporting and must drive it to process exit.
        /// False if it does not. Both false outcomes end in process exit, so the caller
        /// does not continue in production:
        /// <list type="bullet">
        /// <item>The calling thread already owns reporting, because something threw while it
        /// reported and an outer catch block called back in. It exits now. Waiting again
        /// would block on a signal that this same thread has to observe.</item>
        /// <item>Another thread owns reporting. The calling thread blocks until that thread
        /// ends the process, so it does not carry on with a half-initialized mount.</item>
        /// </list>
        /// </returns>
        public bool TryBeginFailure(int exitCode, string message)
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            int previousOwner = Interlocked.CompareExchange(ref this.owningThreadId, currentThreadId, 0);

            if (previousOwner == currentThreadId)
            {
                this.tracer.RelatedWarning(
                    null,
                    $"{nameof(this.TryBeginFailure)}: Re-entered while reporting a mount failure: {message}",
                    Keywords.Telemetry);
                this.environment.ExitProcess(exitCode);
                return false;
            }

            if (previousOwner != 0)
            {
                this.environment.BlockUntilProcessExits();
                return false;
            }

            this.failureMessage = message;
            return true;
        }

        /// <summary>
        /// Sends a status response, and records the outcome only if the send succeeded.
        /// </summary>
        /// <param name="trySend">Sends the response. Returns true if it reached the client.</param>
        /// <param name="carriesMountFailure">True if the response reports MountFailed.</param>
        /// <remarks>
        /// The send must come first. Recording a client that never received the response
        /// would release the wait and let the process exit before any client read the
        /// failure.
        /// </remarks>
        public bool TrySendStatus(Func<bool> trySend, bool carriesMountFailure)
        {
            if (!trySend())
            {
                return false;
            }

            this.clientReceivedStatus = true;

            if (carriesMountFailure)
            {
                this.failureReported.Set();
            }

            return true;
        }

        /// <summary>
        /// Blocks until a client has read the failure, or until a short timeout elapses.
        /// Returns immediately when no client could read it.
        /// </summary>
        /// <returns>True if a client read the failure.</returns>
        public bool WaitForClientToReadFailure()
        {
            if (!this.namedPipeReady)
            {
                return false;
            }

            int timeoutMs = this.clientReceivedStatus ? DefaultReportTimeoutMs : DefaultNoClientTimeoutMs;

            if (this.environment.WaitForSignal(this.failureReported, timeoutMs))
            {
                this.environment.Sleep(DefaultDrainMs);
                return true;
            }

            this.tracer.RelatedWarning(
                null,
                $"{nameof(this.WaitForClientToReadFailure)}: No client read the mount failure within {timeoutMs}ms. Exiting anyway.",
                Keywords.Telemetry);
            return false;
        }
    }
}
