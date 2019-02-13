using System;
using System.Text;

namespace GVFS.Common.Tracing
{
    public abstract class EventListener : IDisposable
    {
        private readonly EventLevel maxVerbosity;
        private readonly Keywords keywordFilter;

        protected EventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.maxVerbosity = maxVerbosity;
            this.keywordFilter = keywordFilter;
        }

        public virtual void Dispose()
        {
        }

        public bool? TryRecordMessage(TraceEventMessage message, out string errorMessage)
        {
            if (this.IsEnabled(message.Level, message.Keywords))
            {
                try
                {
                    this.RecordMessageInternal(message);

                    errorMessage = null;
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.ToString();
                    return false;
                }
            }

            errorMessage = null;
            return null;
        }

        protected abstract void RecordMessageInternal(TraceEventMessage message);

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
