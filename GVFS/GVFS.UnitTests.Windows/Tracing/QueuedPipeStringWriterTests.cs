using System;
using System.IO.Pipes;
using System.Threading;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Moq;
using NUnit.Framework;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class QueuedPipeStringWriterTests
    {
        [TestCase]
        public void Stop_RaisesStateStopped()
        {
            // Capture event invocations
            Mock<IQueuedPipeStringWriterEventSink> eventSink = new Mock<IQueuedPipeStringWriterEventSink>();

            // createPipeFunc returns `null` since the test will never enqueue any messaged to write
            QueuedPipeStringWriter writer = new QueuedPipeStringWriter(
                () => null,
                eventSink.Object);

            // Try to write some dummy data
            writer.Start();
            writer.Stop();

            eventSink.Verify(
                x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Stopped, null),
                Times.Once);
        }

        [TestCase]
        public void MissingPipe_RaisesStateFailing()
        {
            const string inputMessage = "FooBar";

            // Capture event invocations
            Mock<IQueuedPipeStringWriterEventSink> eventSink = new Mock<IQueuedPipeStringWriterEventSink>();

            QueuedPipeStringWriter writer = new QueuedPipeStringWriter(
                () => throw new Exception("Failing pipe connection"),
                eventSink.Object);

            // Try to write some dummy data
            writer.Start();
            bool queueOk = writer.TryEnqueue(inputMessage);
            writer.Stop();

            queueOk.ShouldBeTrue();
            eventSink.Verify(
                x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Failing, It.IsAny<Exception>()),
                Times.Once);
            eventSink.Verify(
                x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Stopped, It.IsAny<Exception>()),
                Times.Once);
        }

        [TestCase]
        public void GoodPipe_WritesDataAndRaisesStateHealthy()
        {
            const string inputMessage = "FooBar";
            byte[] expectedData =
            {
                0x46, 0x6F, 0x6F, 0x42, 0x61, 0x72, (byte)'\n', // "FooBar\n"
                0x46, 0x6F, 0x6F, 0x42, 0x61, 0x72, (byte)'\n', // "FooBar\n"
                0x46, 0x6F, 0x6F, 0x42, 0x61, 0x72, (byte)'\n', // "FooBar\n"
            };

            string pipeName = Guid.NewGuid().ToString("N");

            // Capture event invocations
            Mock<IQueuedPipeStringWriterEventSink> eventSink = new Mock<IQueuedPipeStringWriterEventSink>();

            QueuedPipeStringWriter writer = new QueuedPipeStringWriter(
                () => new NamedPipeClientStream(".", pipeName, PipeDirection.Out),
                eventSink.Object);

            using (TestPipeReaderWorker pipeWorker = new TestPipeReaderWorker(pipeName, PipeTransmissionMode.Byte))
            {
                // Start the pipe reader worker first and wait until the pipe server has been stood-up
                // before starting the pipe writer/enqueuing messages because the writer does not wait
                // for the pipe to be ready to accept (it returns and drops messages immediately).
                pipeWorker.Start();
                pipeWorker.WaitForReadyToAccept();
                writer.Start();

                // Try to write some dummy data
                bool queueOk1 = writer.TryEnqueue(inputMessage);
                bool queueOk2 = writer.TryEnqueue(inputMessage);
                bool queueOk3 = writer.TryEnqueue(inputMessage);

                // Wait until we've received all the sent messages before shuting down
                // the pipe worker thread.
                pipeWorker.WaitForRecievedBytes(count: expectedData.Length);
                pipeWorker.Stop();
                writer.Stop();

                queueOk1.ShouldBeTrue();
                queueOk2.ShouldBeTrue();
                queueOk3.ShouldBeTrue();

                byte[] actualData = pipeWorker.GetReceivedDataSnapshot();
                CollectionAssert.AreEqual(expectedData, actualData);

                // Should only receive one 'healthy' state change per successfully written message
                eventSink.Verify(
                    x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Healthy, It.IsAny<Exception>()),
                    Times.Once);
                eventSink.Verify(
                    x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Stopped, It.IsAny<Exception>()),
                    Times.Once);
            }
        }

        private class TestPipeReaderWorker : IDisposable
        {
            private readonly string pipeName;
            private readonly PipeTransmissionMode transmissionMode;
            private readonly AutoResetEvent receivedData = new AutoResetEvent(initialState: false);
            private readonly ManualResetEvent readyToAccept = new ManualResetEvent(initialState: false);
            private readonly ManualResetEvent shutdownEvent = new ManualResetEvent(initialState: false);

            private int bufferLength = 0;
            private byte[] buffer = new byte[16*1024];
            private object bufferLock = new object();
            private Thread thread;
            private bool isRunning;
            private bool isDisposed;

            public TestPipeReaderWorker(string pipeName, PipeTransmissionMode transmissionMode)
            {
                this.pipeName = pipeName;
                this.transmissionMode = transmissionMode;
            }

            public void Start()
            {
                if (!this.isRunning)
                {
                    this.isRunning = true;
                    this.thread = new Thread(this.ThreadProc)
                    {
                        Name = nameof(TestPipeReaderWorker),
                        IsBackground = true
                    };
                    this.thread.Start();
                }
            }

            public void WaitForReadyToAccept()
            {
                this.readyToAccept.WaitOne();
            }

            public void WaitForRecievedBytes(int count)
            {
                if (!this.isRunning)
                {
                    throw new InvalidOperationException("Worker has been stopped so will never receieve new data");
                }

                int length;

                while (true)
                {
                    // Since the buffer can only grow (and we only care about waiting for a minimum length), we
                    // don't care that the length could increase after we've released the lock.
                    lock (this.bufferLock)
                    {
                        length = this.bufferLength;
                    }

                    if (length >= count)
                    {
                        break;
                    }

                    // Wait for more data (the buffer will grow)
                    this.receivedData.WaitOne();
                }
            }

            public byte[] GetReceivedDataSnapshot()
            {
                if (this.isRunning)
                {
                    throw new InvalidOperationException("Should stop the test pipe worker first");
                }

                if (this.isDisposed)
                {
                    throw new ObjectDisposedException($"{nameof(TestPipeReaderWorker)}");
                }

                byte[] snapshot;

                lock (this.bufferLock)
                {
                    snapshot = new byte[this.bufferLength];
                    Array.Copy(this.buffer, snapshot, snapshot.Length);
                }

                return snapshot;
            }

            public void Stop()
            {
                if (this.isRunning)
                {
                    this.isRunning = false;
                    this.shutdownEvent.Set();
                    this.thread.Join();
                }
            }

            public void Dispose()
            {
                if (this.isDisposed)
                {
                    return;
                }

                this.Stop();
                this.isDisposed = true;
            }

            private void ThreadProc()
            {
                using (NamedPipeServerStream pipe = new NamedPipeServerStream(this.pipeName, PipeDirection.In, -1, this.transmissionMode, PipeOptions.Asynchronous))
                {
                    // Signal that the pipe has been created and we're ready to accept clients
                    this.readyToAccept.Set();

                    pipe.WaitForConnection();

                    while (this.isRunning)
                    {
                        byte[] readBuffer = new byte[1024];

                        IAsyncResult asyncResult = pipe.BeginRead(readBuffer, offset: 0, count: readBuffer.Length, callback: null, state: null);

                        // Wait for a read operation to complete, or until we're told to shutdown
                        WaitHandle.WaitAny(new[] { asyncResult.AsyncWaitHandle, this.shutdownEvent });

                        if (this.isRunning)
                        {
                            // Complete the read
                            int nr = pipe.EndRead(asyncResult);
                            if (nr > 0)
                            {
                                // We actually read some data so append this to the main buffer
                                lock (this.bufferLock)
                                {
                                    Array.Copy(readBuffer, 0, this.buffer, this.bufferLength, nr);
                                    this.bufferLength += nr;
                                }

                                this.receivedData.Set();
                            }
                            else
                            {
                                // We got here because the pipe has been closed.
                                // If we've been asked to shutdown we will break on the next while-loop evaluation.
                            }
                        }
                    }
                }
            }
        }
    }
}
