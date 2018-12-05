using System;

namespace GVFS.Common.Tracing
{
    /// <summary>
    ///   An event listener that will print all telemetry messages to the console with timestamps.
    ///   The format of the message is designed for completeness and parsability, but not for beauty.
    /// </summary>
    public class DiagnosticConsoleEventListener : EventListener
    {
        public DiagnosticConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
            : base(maxVerbosity, keywordFilter)
        {
        }

        protected override void RecordMessageInternal(
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string jsonPayload)
        {
            Console.WriteLine(this.GetLogString(eventName, opcode, jsonPayload));
        }
    }
}