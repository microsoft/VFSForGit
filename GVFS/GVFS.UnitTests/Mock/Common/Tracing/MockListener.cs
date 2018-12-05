using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Common.Tracing
{
    public class MockListener : EventListener
    {
        public MockListener(EventLevel maxVerbosity, Keywords keywordFilter)
            : base(maxVerbosity, keywordFilter)
        {
        }

        public List<string> EventNamesRead { get; set; } = new List<string>();

        protected override void RecordMessageInternal(string eventName, Guid activityId, Guid parentActivityId, EventLevel level, Keywords keywords, EventOpcode opcode, string jsonPayload)
        {
            this.EventNamesRead.Add(eventName);
        }
    }
}
