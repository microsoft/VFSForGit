using System.Collections.Concurrent;

namespace GVFS.Common.Tracing
{
    /// <summary>
    /// An EventListener that buffers telemetry messages in memory.  After
    /// a real listener is attached via <see cref="ReplayAndStop"/>, buffered
    /// messages are replayed and this listener becomes a no-op.
    /// </summary>
    public class BufferingTelemetryListener : EventListener
    {
        public const int DefaultMaxBufferedMessages = 1000;

        private ConcurrentQueue<TraceEventMessage> buffer = new ConcurrentQueue<TraceEventMessage>();
        private readonly int maxBufferedMessages;
        private volatile bool stopped;

        public BufferingTelemetryListener(int maxBufferedMessages = DefaultMaxBufferedMessages)
            : base(EventLevel.Verbose, Keywords.Telemetry, eventSink: null)
        {
            this.maxBufferedMessages = maxBufferedMessages;
        }

        /// <summary>
        /// Number of messages currently buffered.
        /// </summary>
        public int BufferedCount => this.buffer?.Count ?? 0;

        /// <summary>
        /// Whether this listener has been stopped (replay completed).
        /// </summary>
        public bool IsStopped => this.stopped;

        /// <summary>
        /// Replays all buffered messages to <paramref name="target"/> and
        /// stops further buffering.  This listener remains in the tracer's
        /// listener list but becomes a no-op.  Safe to call multiple times;
        /// only the first call replays.
        /// </summary>
        /// <returns>Number of messages replayed.</returns>
        public int ReplayAndStop(EventListener target)
        {
            if (this.stopped)
            {
                return 0;
            }

            this.stopped = true;
            ConcurrentQueue<TraceEventMessage> queue = this.buffer;
            this.buffer = null;

            int count = 0;
            if (queue != null)
            {
                while (queue.TryDequeue(out TraceEventMessage message))
                {
                    target.RecordMessage(message);
                    count++;
                }
            }

            return count;
        }

        protected override void RecordMessageInternal(TraceEventMessage message)
        {
            if (this.stopped)
            {
                return;
            }

            // Soft cap: under high concurrency, a few messages may exceed
            // maxBufferedMessages because Count and Enqueue are not atomic.
            // This is acceptable — the cap prevents unbounded growth, and
            // a small overshoot is harmless.
            ConcurrentQueue<TraceEventMessage> queue = this.buffer;
            if (queue != null && queue.Count < this.maxBufferedMessages)
            {
                queue.Enqueue(message);
            }
        }
    }
}
