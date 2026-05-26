using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common.Tracing;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class BufferingTelemetryListenerTests
    {
        [TestCase]
        public void BuffersAndReplaysMessages()
        {
            BufferingTelemetryListener buffer = new BufferingTelemetryListener();
            MockListener target = new MockListener(EventLevel.Verbose, Keywords.Telemetry);

            TraceEventMessage msg1 = CreateTelemetryMessage("Event1");
            TraceEventMessage msg2 = CreateTelemetryMessage("Event2");
            buffer.RecordMessage(msg1);
            buffer.RecordMessage(msg2);

            buffer.BufferedCount.ShouldEqual(2);

            int replayed = buffer.ReplayAndStop(target);
            replayed.ShouldEqual(2);
            target.EventNamesRead.ShouldContain(name => name.Equals("Event1"));
            target.EventNamesRead.ShouldContain(name => name.Equals("Event2"));
        }

        [TestCase]
        public void StopsBufferingAfterReplay()
        {
            BufferingTelemetryListener buffer = new BufferingTelemetryListener();
            MockListener target = new MockListener(EventLevel.Verbose, Keywords.Telemetry);

            buffer.RecordMessage(CreateTelemetryMessage("Before"));
            buffer.ReplayAndStop(target);

            buffer.IsStopped.ShouldBeTrue();
            buffer.RecordMessage(CreateTelemetryMessage("After"));

            // "After" should not be buffered or replayed
            target.EventNamesRead.Count.ShouldEqual(1);
        }

        [TestCase]
        public void CapsAtMaxBufferedMessages()
        {
            BufferingTelemetryListener buffer = new BufferingTelemetryListener(maxBufferedMessages: 10);
            MockListener target = new MockListener(EventLevel.Verbose, Keywords.Telemetry);

            for (int i = 0; i < 15; i++)
            {
                buffer.RecordMessage(CreateTelemetryMessage($"Event{i}"));
            }

            int replayed = buffer.ReplayAndStop(target);
            replayed.ShouldEqual(10);
            target.EventNamesRead[0].ShouldEqual("Event0");
            target.EventNamesRead[9].ShouldEqual("Event9");
        }

        [TestCase]
        public void SecondReplayReturnsZero()
        {
            BufferingTelemetryListener buffer = new BufferingTelemetryListener();
            MockListener target1 = new MockListener(EventLevel.Verbose, Keywords.Telemetry);
            MockListener target2 = new MockListener(EventLevel.Verbose, Keywords.Telemetry);

            buffer.RecordMessage(CreateTelemetryMessage("Event1"));

            buffer.ReplayAndStop(target1).ShouldEqual(1);
            buffer.ReplayAndStop(target2).ShouldEqual(0);
        }

        private static TraceEventMessage CreateTelemetryMessage(string eventName)
        {
            return new TraceEventMessage
            {
                EventName = eventName,
                Level = EventLevel.Informational,
                Keywords = Keywords.Telemetry,
                Opcode = EventOpcode.Info,
                Payload = "{}"
            };
        }
    }

    [TestFixture]
    public class DeferredTelemetryAttacherTests
    {
        [TestCase]
        public void TryAttach_ReturnsFalseWithNullGitBinRoot()
        {
            using (JsonTracer tracer = new JsonTracer("Test", "NullGitBinRoot", disableTelemetry: true))
            using (DeferredTelemetryAttacher attacher = new DeferredTelemetryAttacher(tracer, "Test", null, null))
            {
                attacher.TryAttach(null).ShouldBeFalse();
                attacher.TryAttach(string.Empty).ShouldBeFalse();
                attacher.IsAttached.ShouldBeFalse();
            }
        }

        [TestCase(0, 10_000)]
        [TestCase(1, 30_000)]
        [TestCase(2, 60_000)]
        [TestCase(3, 300_000)]
        [TestCase(4, 300_000)]
        [TestCase(100, 300_000)]
        public void GetRetryInterval_ReturnsExpectedValues(int retryCount, int expectedMs)
        {
            DeferredTelemetryAttacher.GetRetryInterval(retryCount).ShouldEqual(expectedMs);
        }
    }
}
