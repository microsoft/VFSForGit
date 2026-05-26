using System;
using System.Threading;

namespace GVFS.Common.Tracing
{
    /// <summary>
    /// Manages deferred telemetry pipe attachment for processes that cannot
    /// read the pipe config at startup (e.g. GVFS.Service running as SYSTEM,
    /// or any process started before the telemetry collector is installed).
    ///
    /// Adds a <see cref="BufferingTelemetryListener"/> to the tracer at
    /// construction time, then periodically retries creating a real
    /// <see cref="TelemetryDaemonEventListener"/>.  On success, buffered
    /// messages are replayed and the retry timer stops.
    ///
    /// Callers can also trigger an explicit attach attempt via
    /// <see cref="TryAttach(string)"/> — e.g. on session logon when the
    /// user's HOME is available.
    ///
    /// Designed for reuse by both GVFS.Service and GVFS.Mount.
    /// </summary>
    public class DeferredTelemetryAttacher : IDisposable
    {
        private readonly JsonTracer tracer;
        private readonly BufferingTelemetryListener buffer;
        private readonly string providerName;
        private readonly string enlistmentId;
        private readonly string mountId;
        private readonly Lock attachLock = new Lock();

        private Timer retryTimer;
        private string retryGitBinRoot;
        private int retryCount;
        private bool attached;
        private bool disposed;

        public DeferredTelemetryAttacher(
            JsonTracer tracer,
            string providerName,
            string enlistmentId,
            string mountId)
        {
            this.tracer = tracer;
            this.providerName = providerName;
            this.enlistmentId = enlistmentId;
            this.mountId = mountId;
            this.buffer = new BufferingTelemetryListener();
            tracer.AddEventListener(this.buffer);
        }

        public bool IsAttached
        {
            get
            {
                lock (this.attachLock)
                {
                    return this.attached;
                }
            }
        }

        /// <summary>
        /// Starts a background retry timer that periodically calls
        /// <see cref="TryAttach"/> with the given gitBinRoot.  Uses
        /// exponential backoff: 10s, 30s, 1m, then 5m steady state.
        /// </summary>
        public void StartRetryTimer(string gitBinRoot)
        {
            lock (this.attachLock)
            {
                if (this.attached || this.disposed || this.retryTimer != null)
                {
                    return;
                }

                this.retryGitBinRoot = gitBinRoot;
                this.retryCount = 0;
                this.retryTimer = new Timer(
                    this.OnRetryTimer,
                    null,
                    GetRetryInterval(0),
                    Timeout.Infinite);
            }
        }

        /// <summary>
        /// Attempts to create and attach a TelemetryDaemonEventListener.
        /// Call this when environment conditions change (e.g. user session
        /// becomes available).  Replays buffered messages on success.
        /// Safe to call multiple times — no-ops after first successful attach.
        /// </summary>
        /// <param name="gitBinRoot">Path to git binary.</param>
        /// <param name="globalConfigPath">
        /// If non-null, reads this file with <c>git config --file</c> instead
        /// of <c>--global</c>.  Use this when the caller needs to read another
        /// user's .gitconfig without mutating the process-wide HOME variable.
        /// </param>
        /// <returns>true if attached (now or previously).</returns>
        public bool TryAttach(string gitBinRoot, string globalConfigPath = null)
        {
            lock (this.attachLock)
            {
                if (this.attached || this.tracer.HasTelemetryDaemonListener)
                {
                    return true;
                }

                if (string.IsNullOrEmpty(gitBinRoot))
                {
                    return false;
                }

                TelemetryDaemonEventListener daemonListener;
                try
                {
                    daemonListener = TelemetryDaemonEventListener.CreateIfEnabled(
                        gitBinRoot,
                        this.providerName,
                        this.enlistmentId,
                        this.mountId,
                        this.tracer,
                        globalConfigPath);
                }
                catch (Exception)
                {
                    return false;
                }

                if (daemonListener == null)
                {
                    return false;
                }

                this.tracer.AddEventListener(daemonListener);
                int replayed = this.buffer.ReplayAndStop(daemonListener);
                this.StopRetryTimer();
                this.attached = true;

                this.tracer.RelatedInfo(
                    "DeferredTelemetryAttacher: Attached, replayed {0} buffered messages",
                    replayed);

                return true;
            }
        }

        public void Dispose()
        {
            lock (this.attachLock)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                this.StopRetryTimer();
            }
        }

        internal static int GetRetryInterval(int retryCount)
        {
            return retryCount switch
            {
                0 => 10_000,      // 10 seconds
                1 => 30_000,      // 30 seconds
                2 => 60_000,      // 1 minute
                _ => 300_000,     // 5 minutes
            };
        }

        private void StopRetryTimer()
        {
            // Must be called while holding attachLock
            if (this.retryTimer != null)
            {
                this.retryTimer.Dispose();
                this.retryTimer = null;
            }
        }

        private void OnRetryTimer(object state)
        {
            try
            {
                bool success = this.TryAttach(this.retryGitBinRoot);
                if (!success)
                {
                    lock (this.attachLock)
                    {
                        if (this.retryTimer != null && !this.disposed)
                        {
                            this.retryCount++;
                            this.retryTimer.Change(
                                GetRetryInterval(this.retryCount),
                                Timeout.Infinite);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Swallow — timer will not reschedule, but the explicit
                // TryAttach path (e.g. on SessionLogon) can still succeed.
            }
        }
    }
}
