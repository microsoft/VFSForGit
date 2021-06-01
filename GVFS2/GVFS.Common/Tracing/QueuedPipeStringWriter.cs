using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace GVFS.Common.Tracing
{
    public enum QueuedPipeStringWriterState
    {
        Unknown = 0,
        Stopped = 1,
        Failing = 2,
        Healthy = 3,
    }

    /// <summary>
    /// Accepts string messages from multiple threads and dispatches them over a named pipe from a
    /// background thread.
    /// </summary>
    public class QueuedPipeStringWriter : IDisposable
    {
        private const int DEFAULT_MAX_QUEUE_SIZE = 256;

        private readonly Func<NamedPipeClientStream> createPipeFunc;
        private readonly IQueuedPipeStringWriterEventSink eventSink;
        private readonly BlockingCollection<string> queue;

        private Thread writerThread;
        private NamedPipeClientStream pipeClient;
        private QueuedPipeStringWriterState state = QueuedPipeStringWriterState.Unknown;
        private bool isDisposed;

        public QueuedPipeStringWriter(Func<NamedPipeClientStream> createPipeFunc, IQueuedPipeStringWriterEventSink eventSink, int maxQueueSize = DEFAULT_MAX_QUEUE_SIZE)
        {
            this.createPipeFunc = createPipeFunc;
            this.eventSink = eventSink;
            this.queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), boundedCapacity: maxQueueSize);
        }

        public void Start()
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(QueuedPipeStringWriter));
            }

            if (this.writerThread != null)
            {
                return;
            }

            this.writerThread = new Thread(this.BackgroundWriterThreadProc)
            {
                Name = nameof(QueuedPipeStringWriter),
                IsBackground = true,
            };

            this.writerThread.Start();
        }

        public bool TryEnqueue(string message)
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(QueuedPipeStringWriter));
            }

            return this.queue.TryAdd(message);
        }

        public void Stop()
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(QueuedPipeStringWriter));
            }

            if (this.queue.IsAddingCompleted)
            {
                return;
            }

            // Signal to the queue draining thread that it should drain once more and then terminate.
            this.queue.CompleteAdding();
            this.writerThread.Join();

            Debug.Assert(this.queue.IsCompleted, "Message queue should be empty after being stopped");
        }

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.Stop();

                this.pipeClient?.Dispose();
                this.pipeClient = null;

                this.writerThread = null;

                this.queue.Dispose();
            }

            this.isDisposed = true;
        }

        private void RaiseStateChanged(QueuedPipeStringWriterState newState, Exception ex)
        {
            if (this.state != newState)
            {
                this.state = newState;
                this.eventSink?.OnStateChanged(this, newState, ex);
            }
        }

        private void BackgroundWriterThreadProc()
        {
            // Drain the queue of all messages currently in the queue.
            // TryTake() using an infinite timeout will block until either a message is available (returns true)
            // or the queue has been marked as completed _and_ is empty (returns false).
            string message;
            while (this.queue.TryTake(out message, Timeout.Infinite))
            {
                if (message != null)
                {
                    this.WriteMessage(message);
                }
            }

            this.RaiseStateChanged(QueuedPipeStringWriterState.Stopped, null);
        }

        private void WriteMessage(string message)
        {
            // Create pipe if this is the first message, or if the last connection broke for any reason
            if (this.pipeClient == null)
            {
                try
                {
                    // Create a new pipe stream instance using the provided factory
                    NamedPipeClientStream pipe = this.createPipeFunc();

                    // Specify a instantaneous timeout because we don't want to hold up the
                    // background thread loop if the pipe is not available; we will just drop this event.
                    // The pipe server should already be running and waiting for connections from us.
                    pipe.Connect(timeout: 0);

                    // Keep a hold of this connected pipe for future messages
                    this.pipeClient = pipe;
                }
                catch (Exception ex)
                {
                    this.RaiseStateChanged(QueuedPipeStringWriterState.Failing, ex);
                    return;
                }
            }

            try
            {
                // If we're in byte/stream transmission mode rather than message mode
                // we should signal the end of each message with a line-feed (LF) character.
                if (this.pipeClient.TransmissionMode == PipeTransmissionMode.Byte)
                {
                    message += '\n';
                }

                byte[] data = Encoding.UTF8.GetBytes(message);
                this.pipeClient.Write(data, 0, data.Length);
                this.pipeClient.Flush();

                this.RaiseStateChanged(QueuedPipeStringWriterState.Healthy, null);
            }
            catch (Exception ex)
            {
                // We can't send this message for some reason (e.g., broken pipe); we attempt no recovery or retry
                // mechanism and drop this message. We will try to recreate/connect the pipe on the next message.
                this.pipeClient.Dispose();
                this.pipeClient = null;
                this.RaiseStateChanged(QueuedPipeStringWriterState.Failing, ex);
                return;
            }
        }
    }
}
