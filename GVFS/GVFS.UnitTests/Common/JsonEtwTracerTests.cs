using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Microsoft.Diagnostics.Tracing;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class JsonEtwTracerTests
    {
        [TestCase]
        public void EventsAreFilteredByVerbosity()
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity"))
            using (MockListener listener = new MockListener(EventLevel.Informational, Keywords.Any))
            {
                listener.EnableEvents(tracer.EvtSource, EventLevel.Verbose);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity"))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                listener.EnableEvents(tracer.EvtSource, EventLevel.Verbose);

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
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity"))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Network))
            {
                listener.EnableEvents(tracer.EvtSource, EventLevel.Verbose);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            // Any filters nothing out
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity"))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                listener.EnableEvents(tracer.EvtSource, EventLevel.Verbose);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldAlsoReceive"));
            }
             
            // None filters everything out (including events marked as none)
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity"))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.None))
            {
                listener.EnableEvents(tracer.EvtSource, EventLevel.Verbose);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldNotReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldBeEmpty();

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoNotReceive", metadata: null);
                listener.EventNamesRead.ShouldBeEmpty();
            }
        }

        public class MockListener : ConsoleEventListener
        {
            public readonly List<string> EventNamesRead = new List<string>();

            public MockListener(EventLevel maxVerbosity, Keywords keywordFilter) : base(maxVerbosity, keywordFilter)
            {
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (!this.IsEnabled(eventData.Level, eventData.Keywords))
                {
                    return;
                }

                this.EventNamesRead.Add(eventData.EventName);
            }
        }
    }
}
