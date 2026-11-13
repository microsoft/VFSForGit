using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Moq;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class QueuedPipeStringWriterTests
    {
        [TestCase]
        public async Task GoodPipe_WritesDataAndRaisesStateHealthy()
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

            using (TestPipeReaderWorker pipeWorker = new TestPipeReaderWorker(pipeName, PipeTransmissionMode.Byte))
            {
                // Start the pipe reader worker first and wait until the pipe server has been stood-up
                // before starting the pipe writer/enqueuing messages because the writer does not wait
                // for the pipe to be ready to accept (it returns and drops messages immediately).
                pipeWorker.Start();
                pipeWorker.WaitForReadyToAccept();
                QueuedPipeStringWriter writer = new QueuedPipeStringWriter(
                    () => new NamedPipeClientStream(".", pipeName, PipeDirection.Out),
                    eventSink.Object);
                writer.Start();
                Task stop = null;
                try
                {
                    bool queueOk1 = writer.TryEnqueue(inputMessage);
                    bool queueOk2 = writer.TryEnqueue(inputMessage);
                    bool queueOk3 = writer.TryEnqueue(inputMessage);

                    pipeWorker.WaitForRecievedBytes(count: expectedData.Length);
                    stop = Task.Run(writer.Stop);
                    await stop.WaitAsync(TimeSpan.FromSeconds(5));
                    pipeWorker.Stop();

                    queueOk1.ShouldBeTrue();
                    queueOk2.ShouldBeTrue();
                    queueOk3.ShouldBeTrue();

                    byte[] actualData = pipeWorker.GetReceivedDataSnapshot();
                    CollectionAssert.AreEqual(expectedData, actualData);

                    eventSink.Verify(
                        x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Healthy, It.IsAny<Exception>()),
                        Times.Once);
                    eventSink.Verify(
                        x => x.OnStateChanged(writer, QueuedPipeStringWriterState.Stopped, It.IsAny<Exception>()),
                        Times.Once);
                }
                finally
                {
                    try
                    {
                        pipeWorker.Stop();
                    }
                    finally
                    {
                        try
                        {
                            if (stop != null)
                            {
                                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                            }
                        }
                        finally
                        {
                            await Task.Run(writer.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
                        }
                    }
                }
            }
        }

        private class TestPipeReaderWorker : IDisposable
        {
            private readonly NamedPipeServerStream pipe;
            private readonly CancellationTokenSource shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            private readonly AutoResetEvent receivedData = new AutoResetEvent(initialState: false);
            private readonly TaskCompletionSource<bool> readyToAccept =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private int bufferLength = 0;
            private readonly byte[] buffer = new byte[16 * 1024];
            private readonly Lock bufferLock = new Lock();
            private Task worker;
            private bool isRunning;
            private bool isDisposed;

            public TestPipeReaderWorker(string pipeName, PipeTransmissionMode transmissionMode)
            {
                this.pipe = new NamedPipeServerStream(
                    pipeName, PipeDirection.In, 1, transmissionMode, PipeOptions.Asynchronous);
            }

            public void Start()
            {
                if (!this.isRunning)
                {
                    this.isRunning = true;
                    this.worker = this.ReadAsync();
                }
            }

            public void WaitForReadyToAccept()
            {
                this.readyToAccept.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }

            public void WaitForRecievedBytes(int count)
            {
                if (!this.isRunning)
                {
                    throw new InvalidOperationException("The pipe worker is stopped.");
                }

                int length;

                Stopwatch elapsed = Stopwatch.StartNew();
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

                    TimeSpan remaining = TimeSpan.FromSeconds(5) - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero || !this.receivedData.WaitOne(remaining))
                    {
                        throw new TimeoutException("The pipe worker did not receive all expected bytes.");
                    }
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
                    this.shutdown.Cancel();
                    this.pipe.Dispose();
                    try
                    {
                        this.worker.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (this.shutdown.IsCancellationRequested)
                    {
                    }
                    catch (ObjectDisposedException) when (this.shutdown.IsCancellationRequested)
                    {
                    }
                }
            }

            public void Dispose()
            {
                if (this.isDisposed)
                {
                    return;
                }

                try
                {
                    this.Stop();
                }
                finally
                {
                    this.pipe.Dispose();
                    this.shutdown.Dispose();
                    this.receivedData.Dispose();
                    this.isDisposed = true;
                }
            }

            private async Task ReadAsync()
            {
                this.readyToAccept.TrySetResult(true);
                await this.pipe.WaitForConnectionAsync(this.shutdown.Token);
                byte[] readBuffer = new byte[1024];
                while (true)
                {
                    int count = await this.pipe.ReadAsync(readBuffer.AsMemory(), this.shutdown.Token);
                    if (count == 0)
                    {
                        return;
                    }

                    lock (this.bufferLock)
                    {
                        Array.Copy(readBuffer, 0, this.buffer, this.bufferLength, count);
                        this.bufferLength += count;
                    }

                    this.receivedData.Set();
                }
            }
        }
    }
}
