using System;

namespace GVFS.Common.Tracing
{
    public class TraceEventMessage
    {
        public string EventName { get; set; }
        public Guid ActivityId { get; set; }
        public Guid ParentActivityId { get; set; }
        public EventLevel Level { get; set; }
        public Keywords Keywords { get; set; }
        public EventOpcode Opcode { get; set; }
        public string Payload { get; set; }
    }
}
