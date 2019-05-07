using System;

namespace GVFS.Common.Tracing
{
    /// <summary>
    ///   An event listener that will print all telemetry messages to the console with timestamps.
    ///   The format of the message is designed for completeness and parsability, but not for beauty.
    /// </summary>
    public class DiagnosticConsoleEventListener : EventListener
    {
        public DiagnosticConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter, IEventListenerEventSink eventSink)
            : base(maxVerbosity, keywordFilter, eventSink)
        {
        }

        protected override void RecordMessageInternal(TraceEventMessage message)
        {
            Console.WriteLine(this.GetLogString(message.EventName, message.Opcode, message.Payload));
        }
    }
}
