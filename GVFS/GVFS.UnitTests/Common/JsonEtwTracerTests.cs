using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Microsoft.Diagnostics.Tracing;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class JsonEtwTracerTests
    {
        [TestCase]
        public void EventsAreFilteredByVerbosity()
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity1", useCriticalTelemetryFlag: false))
            using (MockListener listener = new MockListener(EventLevel.Informational, Keywords.Any))
            {
                tracer.AddInProcEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByVerbosity2", useCriticalTelemetryFlag: false))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                tracer.AddInProcEventListener(listener);

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
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword1", useCriticalTelemetryFlag: false))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Network))
            {
                tracer.AddInProcEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldNotReceive", metadata: null);
                listener.EventNamesRead.ShouldNotContain(name => name.Equals("ShouldNotReceive"));
            }

            // Any filters nothing out
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword2", useCriticalTelemetryFlag: false))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.Any))
            {
                tracer.AddInProcEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldReceive"));

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoReceive", metadata: null);
                listener.EventNamesRead.ShouldContain(name => name.Equals("ShouldAlsoReceive"));
            }
             
            // None filters everything out (including events marked as none)
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft-GVFS-Test", "EventsAreFilteredByKeyword3", useCriticalTelemetryFlag: false))
            using (MockListener listener = new MockListener(EventLevel.Verbose, Keywords.None))
            {
                tracer.AddInProcEventListener(listener);

                tracer.RelatedEvent(EventLevel.Informational, "ShouldNotReceive", metadata: null, keyword: Keywords.Network);
                listener.EventNamesRead.ShouldBeEmpty();

                tracer.RelatedEvent(EventLevel.Verbose, "ShouldAlsoNotReceive", metadata: null);
                listener.EventNamesRead.ShouldBeEmpty();
            }
        }

        public class MockListener : InProcEventListener
        {
            public readonly List<string> EventNamesRead = new List<string>();

            public MockListener(EventLevel maxVerbosity, Keywords keywordFilter)
                : base(maxVerbosity, keywordFilter)
            {
            }

            protected override void RecordMessageInternal(string eventName, Guid activityId, Guid parentActivityId, EventLevel level, Keywords keywords, EventOpcode opcode, string jsonPayload)
            {
                this.EventNamesRead.Add(eventName);
            }
        }
    }
}
