using System;
using Microsoft.Diagnostics.Tracing;

namespace GVFS.Common.Tracing
{
    public class ConsoleEventListener : InProcEventListener
    {
        public ConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
            : base(maxVerbosity, keywordFilter)
        {
        }

        public override void RecordMessage(string message)
        {
            Console.WriteLine(message);
        }
    }
}