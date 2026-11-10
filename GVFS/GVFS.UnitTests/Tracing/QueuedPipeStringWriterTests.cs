using System;
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
    }
}
