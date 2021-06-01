using System;
using Newtonsoft.Json;

namespace GVFS.Common.Tracing
{
    /// <summary>
    ///   An event listener that will print any message that it can nicely format for the console
    ///   that matches the verbosity level it is given.  At the moment, this means only messages
    ///   with an "ErrorMessage" attribute will get displayed.
    /// </summary>
    public class PrettyConsoleEventListener : EventListener
    {
        private static object consoleLock = new object();

        public PrettyConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter, IEventListenerEventSink eventSink)
            : base(maxVerbosity, keywordFilter, eventSink)
        {
        }

        protected override void RecordMessageInternal(TraceEventMessage message)
        {
            if (string.IsNullOrEmpty(message.Payload))
            {
                return;
            }

            ConsoleOutputPayload payload = JsonConvert.DeserializeObject<ConsoleOutputPayload>(message.Payload);
            if (string.IsNullOrEmpty(payload.ErrorMessage))
            {
                return;
            }

            // It's necessary to do a lock here because this can be called in a multi-threaded
            // environment and we want to make sure that ForegroundColor is restored correctly.
            lock (consoleLock)
            {
                ConsoleColor prevColor = Console.ForegroundColor;
                string prefix;
                switch (message.Level)
                {
                    case EventLevel.Critical:
                    case EventLevel.Error:
                    case EventLevel.LogAlways:
                        prefix = "Error";
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case EventLevel.Warning:
                        prefix = "Warning";
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    default:
                        prefix = "Info";
                        break;
                }

                // The leading \r interacts with the spinner, which always leaves the
                //  cursor at the end of the line, rather than the start.
                Console.WriteLine($"\r{prefix}: {payload.ErrorMessage}");
                Console.ForegroundColor = prevColor;
            }
        }

        private class ConsoleOutputPayload
        {
            public string ErrorMessage { get; set; }
        }
    }
}