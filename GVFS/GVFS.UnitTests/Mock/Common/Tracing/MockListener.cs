using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Common.Tracing
{
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
