using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common.Tracing;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class JsonTracerTests
    {
        [TestCase]
        public void EventsAreFilteredByVerbosity()
        {
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity1", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Informational, Keywords.Any))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity2", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldAlsoReceive"));
            }
        }

        [TestCase]
        public void EventsAreFilteredByKeyword()
        {
            // Network filters all but network out
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword1", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Network))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            // Any filters nothing out
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword2", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldAlsoReceive"));
            }

            // None filters everything out (including events marked as none)
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword3", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.None))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldNotReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldBeEmpty();

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoNotReceive", metadata: null);
                listener.EventNamesRead.ShouldBeEmpty();
            }
        }

        [TestCase]
        public void EventMetadataWithKeywordsIsOptional()
        {
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "EventMetadataWithKeywordsIsOptional", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                tracer.AddEventListener(listener);

                tracer.RelatedWarning(metadata: null, message: string.Empty, keywords: Keywords.Telemetry);
                listener.EventNamesRead.ShouldContain(x => x.Equals("Warning"));

                tracer.RelatedError(metadata: null, message: string.Empty, keywords: Keywords.Telemetry);
                listener.EventNamesRead.ShouldContain(x => x.Equals("Error"));
            }
        }

        [TestCase]
        public void StartEventDoesNotDispatchTelemetry()
        {
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "StartEventDoesNotDispatchTelemetry", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Telemetry))
            {
                tracer.AddEventListener(listener);

                using (ITracer activity = tracer.StartActivity("TestActivity", EventLevel.Informational, Keywords.Telemetry, null))
                {
                    listener.EventNamesRead.ShouldBeEmpty();

                    activity.Stop(null);
                    listener.EventNamesRead.ShouldContain(x => x.Equals("TestActivity"));
                }
            }
        }

        [TestCase]
        public void StopEventIsDispatchedOnDispose()
        {
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "StopEventIsDispatchedOnDispose", disableTelemetry: true))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Telemetry))
            {
                tracer.AddEventListener(listener);

                using (ITracer activity = tracer.StartActivity("TestActivity", EventLevel.Informational, Keywords.Telemetry, null))
                {
                    listener.EventNamesRead.ShouldBeEmpty();
                }

                listener.EventNamesRead.ShouldContain(x => x.Equals("TestActivity"));
            }
        }
    }
}
