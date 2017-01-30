using Microsoft.Diagnostics.Tracing;
using System;
using System.Text;

namespace GVFS.Common.Tracing
{
    public abstract class InProcEventListener : EventListener
    {
        private EventLevel maxVerbosity;
        private EventKeywords keywordFilter;

        public InProcEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.maxVerbosity = maxVerbosity;
            this.keywordFilter = (EventKeywords)keywordFilter;
        }

        public abstract void RecordMessage(string message);

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!this.IsEnabled(eventData.Level, eventData.Keywords))
            {
                return;
            }

            StringBuilder eventLine = new StringBuilder();
            eventLine.AppendFormat("[{0}] {1}", DateTime.Now, eventData.EventName);
            if (eventData.Opcode != 0)
            {
                eventLine.AppendFormat(" ({0})", eventData.Opcode);
            }

            if (eventData.Payload != null)
            {
                eventLine.Append(":");

                for (int i = 0; i < eventData.PayloadNames.Count; i++)
                {
                    // Space prefix avoids a string.Join.
                    eventLine.AppendFormat(" {0}: {1}", eventData.PayloadNames[i], eventData.Payload[i]);
                }
            }

            this.RecordMessage(eventLine.ToString());
        }

        protected bool IsEnabled(EventLevel level, EventKeywords keyword)
        {
            // Strip the sampling keyword since it is always present and will cause false positives
            EventKeywords correctedKeyword = keyword & (EventKeywords)~Keywords.NoAsimovSampling;

            return this.keywordFilter != (EventKeywords)Keywords.None &&
                this.maxVerbosity >= level &&
                this.keywordFilter.HasFlag(correctedKeyword);
        }
    }
}
