using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GVFS.Common.Tracing
{
    public class JsonEtwTracer : ITracer
    {
        public const string NetworkErrorEventName = "NetworkError";

        private List<InProcEventListener> listeners = new List<InProcEventListener>();

        private string activityName;
        private Guid parentActivityId;
        private Guid activityId;
        private bool stopped = false;
        private Stopwatch duration = Stopwatch.StartNew();

        private EventLevel startStopLevel;
        private Keywords startStopKeywords;

        public JsonEtwTracer(string providerName, string activityName)
            : this(providerName, Guid.Empty, activityName)
        {
        }

        public JsonEtwTracer(string providerName, Guid providerActivityId, string activityName)
            : this(
                  new List<InProcEventListener>(),
                  providerActivityId,
                  activityName,
                  EventLevel.Informational,
                  Keywords.Telemetry)
        {
        }

        private JsonEtwTracer(List<InProcEventListener> listeners, Guid parentActivityId, string activityName, EventLevel startStopLevel, Keywords startStopKeywords)
        {
            this.listeners = listeners;
            this.parentActivityId = parentActivityId;
            this.activityName = activityName;
            this.startStopLevel = startStopLevel;
            this.startStopKeywords = startStopKeywords;

            this.activityId = Guid.NewGuid();
        }

        public void AddInProcEventListener(InProcEventListener listener)
        {
            this.listeners.Add(listener);
        }

        public void AddDiagnosticConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.listeners.Add(new DiagnosticConsoleEventListener(maxVerbosity, keywordFilter));
        }

        public void AddPrettyConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.listeners.Add(new PrettyConsoleEventListener(maxVerbosity, keywordFilter));
        }

        public void AddLogFileEventListener(string logFilePath, EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.listeners.Add(new LogFileEventListener(logFilePath, maxVerbosity, keywordFilter));
        }

        public void Dispose()
        {
            this.Stop(null);

            // If we have no parent, then we are the root tracer and should dispose our eventsource.
            if (this.parentActivityId == Guid.Empty)
            {
                foreach (InProcEventListener listener in this.listeners)
                {
                    listener.Dispose();
                }

                this.listeners.Clear();
            }
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata)
        {
            this.RelatedEvent(level, eventName, metadata, Keywords.None);
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keyword)
        {
            this.WriteEvent(eventName, level, keyword, metadata, opcode: 0);
        }

        public virtual void RelatedError(EventMetadata metadata)
        {
            this.RelatedError(metadata, Keywords.Telemetry);
        }

        public virtual void RelatedError(EventMetadata metadata, Keywords keywords)
        {
            this.RelatedEvent(EventLevel.Error, GetCategorizedErrorEventName(keywords), metadata, keywords | Keywords.Telemetry);
        }

        public virtual void RelatedError(string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ErrorMessage", message);
            this.RelatedError(metadata);
        }

        public virtual void RelatedError(string format, params object[] args)
        {
            this.RelatedError(string.Format(format, args));
        }

        public void Stop(EventMetadata metadata)
        {
            if (this.stopped)
            {
                return;
            }

            this.duration.Stop();
            this.stopped = true;

            metadata = metadata ?? new EventMetadata();
            metadata.Add("DurationMs", this.duration.ElapsedMilliseconds);

            this.WriteEvent(this.activityName, this.startStopLevel, this.startStopKeywords, metadata, EventOpcode.Stop);
        }

        public ITracer StartActivity(string childActivityName, EventLevel startStopLevel)
        {
            return this.StartActivity(childActivityName, startStopLevel, null);
        }

        public ITracer StartActivity(string childActivityName, EventLevel startStopLevel, EventMetadata startMetadata)
        {
            return this.StartActivity(childActivityName, startStopLevel, Keywords.None, startMetadata);
        }

        public ITracer StartActivity(string childActivityName, EventLevel startStopLevel, Keywords startStopKeywords, EventMetadata startMetadata)
        {
            JsonEtwTracer subTracer = new JsonEtwTracer(this.listeners, this.activityId, childActivityName, startStopLevel, startStopKeywords);
            subTracer.WriteStartEvent(startMetadata, startStopKeywords);

            return subTracer;
        }

        public void WriteStartEvent(
            string enlistmentRoot,
            string repoUrl,
            string cacheServerUrl,
            EventMetadata additionalMetadata = null)
        {
            EventMetadata metadata = new EventMetadata();

            metadata.Add("Version", ProcessHelper.GetCurrentProcessVersion());

            if (enlistmentRoot != null)
            {
                metadata.Add("EnlistmentRoot", enlistmentRoot);
            }

            if (repoUrl != null)
            {
                metadata.Add("Remote", Uri.EscapeUriString(repoUrl));
            }

            if (cacheServerUrl != null)
            {
                // Changing this key to CacheServerUrl will mess with our telemetry, so it stays for historical reasons
                metadata.Add("ObjectsEndpoint", Uri.EscapeUriString(cacheServerUrl));
            }

            if (additionalMetadata != null)
            {
                foreach (string key in additionalMetadata.Keys)
                {
                    metadata.Add(key, additionalMetadata[key]);
                }
            }

            this.WriteStartEvent(metadata, Keywords.Telemetry);
        }

        public void WriteStartEvent(EventMetadata metadata, Keywords keywords)
        {
            this.WriteEvent(this.activityName, this.startStopLevel, keywords, metadata, EventOpcode.Start);
        }

        private static string GetCategorizedErrorEventName(Keywords keywords)
        {
            switch (keywords)
            {
                case Keywords.Network: return NetworkErrorEventName;
                default: return "Error";
            }
        }

        private void WriteEvent(string eventName, EventLevel level, Keywords keywords, EventMetadata metadata, EventOpcode opcode)
        {
            string jsonPayload = metadata != null ? JsonConvert.SerializeObject(metadata) : null;

            foreach (InProcEventListener listener in this.listeners)
            {
                listener.RecordMessage(eventName, this.activityId, this.parentActivityId, level, keywords, opcode, jsonPayload);
            }
        }
    }
}