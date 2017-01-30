using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GVFS.Common.Tracing
{
    public class JsonEtwTracer : ITracer
    {
        public const string NetworkErrorEventName = "NetworkError";

        private string activityName;
        private Guid parentActivityId;
        private Guid activityId;
        private bool stopped = false;
        private Stopwatch duration = Stopwatch.StartNew();

        private EventLevel startStopLevel;
        private List<InProcEventListener> listeners;

        public JsonEtwTracer(string providerName, string activityName)
            : this(
                  new EventSource(providerName, EventSourceSettings.EtwSelfDescribingEventFormat),
                  Guid.Empty,
                  activityName,
                  EventLevel.Informational)
        {
            this.listeners = new List<InProcEventListener>();
        }

        private JsonEtwTracer(
            EventSource eventSource,
            Guid parentActivityId,
            string activityName,
            EventLevel startStopLevel)
        {
            this.EvtSource = eventSource;
            this.parentActivityId = parentActivityId;
            this.activityName = activityName;
            this.startStopLevel = startStopLevel;

            this.activityId = Guid.NewGuid();
        }

        public EventSource EvtSource { get; }

        public static string GetNameFromEnlistmentPath(string enlistmentRootPath)
        {
            return "Microsoft-GVFS_" + enlistmentRootPath.ToUpper().Replace(':', '_');
        }

        public void AddConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.AddEventListener(
                new ConsoleEventListener(maxVerbosity, keywordFilter),
                maxVerbosity);
        }

        public void AddLogFileEventListener(string logFilePath, EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.AddEventListener(
                new LogFileEventListener(logFilePath, maxVerbosity, keywordFilter),
                maxVerbosity);
        }

        public void Dispose()
        {
            this.Stop(null);

            // If we have no parent, then we are the root tracer and should dispose our eventsource.
            if (this.parentActivityId == Guid.Empty)
            {
                if (this.listeners != null)
                {
                    foreach (InProcEventListener listener in this.listeners)
                    {
                        listener.Dispose();
                    }

                    this.listeners = null;
                }

                this.EvtSource.Dispose();
            }
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata)
        {
            this.RelatedEvent(level, eventName, metadata, Keywords.None);
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keyword)
        {
            EventSourceOptions options = this.CreateDefaultOptions(level, keyword);
            this.WriteEvent(eventName, metadata, ref options);
        }

        public virtual void RelatedError(EventMetadata metadata)
        {
            this.RelatedError(metadata, Keywords.None);
        }

        public virtual void RelatedError(EventMetadata metadata, Keywords keywords)
        {
            this.RelatedEvent(EventLevel.Error, GetCategorizedErrorEventName(keywords), metadata, keywords);
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

            EventSourceOptions options = this.CreateDefaultOptions(this.startStopLevel, Keywords.None);
            options.Opcode = EventOpcode.Stop;

            metadata = metadata ?? new EventMetadata();
            metadata.Add("DurationMs", this.duration.ElapsedMilliseconds);

            this.WriteEvent(this.activityName, metadata, ref options);
        }
        
        public ITracer StartActivity(string childActivityName, EventLevel startStopLevel)
        {
            return this.StartActivity(childActivityName, startStopLevel, null);
        }

        public ITracer StartActivity(string childActivityName, EventLevel startStopLevel, EventMetadata startMetadata)
        {
            JsonEtwTracer subTracer = new JsonEtwTracer(this.EvtSource, this.activityId, childActivityName, startStopLevel);
            subTracer.WriteStartEvent(startMetadata);

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

            this.WriteStartEvent(metadata);
        }
        
        public void WriteStartEvent(EventMetadata metadata)
        {
            EventSourceOptions options = this.CreateDefaultOptions(this.startStopLevel, Keywords.None);
            options.Opcode = EventOpcode.Start;

            this.WriteEvent(this.activityName, metadata, ref options);
        }

        private static string GetCategorizedErrorEventName(Keywords keywords)
        {
            switch (keywords)
            {
                case Keywords.Network: return NetworkErrorEventName;
                default: return "Error";
            }
        }
        
        private void WriteEvent(string eventName, EventMetadata metadata, ref EventSourceOptions options)
        {
            if (metadata != null)
            {
                JsonPayload payload = new JsonPayload(metadata);
                EventSource.SetCurrentThreadActivityId(this.activityId);
                this.EvtSource.Write(eventName, ref options, ref this.activityId, ref this.parentActivityId, ref payload);
            }
            else
            {
                EmptyStruct payload = new EmptyStruct();
                EventSource.SetCurrentThreadActivityId(this.activityId);
                this.EvtSource.Write(eventName, ref options, ref this.activityId, ref this.parentActivityId, ref payload);
            }
        }

        private EventSourceOptions CreateDefaultOptions(EventLevel level, Keywords keywords)
        {
            EventSourceOptions options = new EventSourceOptions();
            options.Keywords = (EventKeywords)(Keywords.NoAsimovSampling | keywords);
            options.Level = level;
            return options;
        }

        private void AddEventListener(InProcEventListener listener, EventLevel maxVerbosity)
        {
            if (this.listeners == null)
            {
                throw new InvalidOperationException("You can only register a listener on the root tracer object");
            }

            if (maxVerbosity >= EventLevel.Verbose)
            {
                listener.RecordMessage(string.Format("ETW Provider name: {0} ({1})", this.EvtSource.Name, this.EvtSource.Guid));
                listener.RecordMessage("Activity Id: " + this.activityId);
            }

            listener.EnableEvents(this.EvtSource, maxVerbosity);
            this.listeners.Add(listener);
        }

        // Needed to pass relatedId without metadata
        [EventData]
        private struct EmptyStruct
        {
        }
        
        [EventData]
        private struct JsonPayload
        {
            public JsonPayload(object serializableObject)
            {
                this.Json = JsonConvert.SerializeObject(serializableObject);
            }

            [EventField]
            public string Json { get; }
        }
    }
}