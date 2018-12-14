using System;
using System.Text;

namespace GVFS.Common.Tracing
{
    public abstract class InProcEventListener : IDisposable
    {
        private EventLevel maxVerbosity;
        private Keywords keywordFilter;

        public InProcEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.maxVerbosity = maxVerbosity;
            this.keywordFilter = keywordFilter;
        }

        public virtual void Dispose()
        {
        }

        public void RecordMessage(string eventName, Guid activityId, Guid parentActivityId, EventLevel level, Keywords keywords, EventOpcode opcode, string jsonPayload)
        {
            if (!this.IsEnabled(level, keywords))
            {
                return;
            }

            this.RecordMessageInternal(eventName, activityId, parentActivityId, level, keywords, opcode, jsonPayload);
        }

        protected abstract void RecordMessageInternal(
            string eventName,
            Guid activityId,
            Guid parentActivityId,
            EventLevel level,
            Keywords keywords,
            EventOpcode opcode,
            string payload);

        protected string GetLogString(string eventName, EventOpcode opcode, string jsonPayload)
        {
            // Make a smarter guess (than 16 characters) about initial size to reduce allocations
            StringBuilder message = new StringBuilder(1024);
            message.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss zzz}] {1}", DateTime.Now, eventName);

            if (opcode != 0)
            {
                message.Append(" (" + opcode + ")");
            }

            if (!string.IsNullOrEmpty(jsonPayload))
            {
                message.Append(" " + jsonPayload);
            }

            return message.ToString();
        }

        protected bool IsEnabled(EventLevel level, Keywords keyword)
        {
            return this.keywordFilter != Keywords.None &&
                this.maxVerbosity >= level &&
                (this.keywordFilter & keyword) != 0;
        }
    }
}
