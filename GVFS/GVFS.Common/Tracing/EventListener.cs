using System;
using System.Text;

namespace GVFS.Common.Tracing
{
    public abstract class EventListener : IDisposable
    {
        private readonly EventLevel maxVerbosity;
        private readonly Keywords keywordFilter;
        private readonly IEventListenerEventSink eventSink;

        protected EventListener(EventLevel maxVerbosity, Keywords keywordFilter, IEventListenerEventSink eventSink)
        {
            this.maxVerbosity = maxVerbosity;
            this.keywordFilter = keywordFilter;
            this.eventSink = eventSink;
        }

        public virtual void Dispose()
        {
        }

        public void RecordMessage(TraceEventMessage message)
        {
            if (this.IsEnabled(message.Level, message.Keywords))
            {
                try
                {
                    this.RecordMessageInternal(message);
                }
                catch (Exception ex)
                {
                    this.RaiseListenerFailure(ex.ToString());
                }
            }
        }

        protected abstract void RecordMessageInternal(TraceEventMessage message);

        protected string GetLogString(string eventName, EventOpcode opcode, string jsonPayload)
        {
            // Make a smarter guess (than 16 characters) about initial size to reduce allocations
            StringBuilder message = new StringBuilder(1024);
            message.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss.ffff zzz}] {1}", DateTime.Now, eventName);

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

        protected void RaiseListenerRecovery()
        {
            this.eventSink?.OnListenerRecovery(this);
        }

        protected void RaiseListenerFailure(string errorMessage)
        {
            this.eventSink?.OnListenerFailure(this, errorMessage);
        }
    }
}
