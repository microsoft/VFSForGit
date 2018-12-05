using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GVFS.Common.Tracing
{
    public class JsonTracer : ITracer
    {
        public const string NetworkErrorEventName = "NetworkError";

        private List<EventListener> listeners = new List<EventListener>();

        private string activityName;
        private Guid parentActivityId;
        private Guid activityId;
        private bool stopped = false;
        private Stopwatch duration = Stopwatch.StartNew();

        private EventLevel startStopLevel;
        private Keywords startStopKeywords;

        private bool disposed;

        public JsonTracer(string providerName, string activityName, bool disableTelemetry = false)
            : this(providerName, Guid.Empty, activityName, enlistmentId: null, mountId: null, disableTelemetry: disableTelemetry)
        {
        }

        public JsonTracer(string providerName, string activityName, string enlistmentId, string mountId, bool disableTelemetry = false)
            : this(providerName, Guid.Empty, activityName, enlistmentId, mountId, disableTelemetry)
        {
        }

        public JsonTracer(string providerName, Guid providerActivityId, string activityName, string enlistmentId, string mountId, bool disableTelemetry = false)
            : this(
                  new List<EventListener>(),
                  providerActivityId,
                  activityName,
                  EventLevel.Informational,
                  Keywords.Telemetry)
        {
            if (!disableTelemetry)
            {
                string gitBinRoot = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
                EventListener commonListener = TelemetryDaemonEventListener.CreateIfEnabled(gitBinRoot, providerName, enlistmentId, mountId);
                if (commonListener != null)
                {
                    this.listeners.Add(commonListener);
                }

                EventListener platformListener = GVFSPlatform.Instance.CreatePlatformTelemetryListener(providerName, enlistmentId, mountId);
                if (platformListener != null)
                {
                    this.listeners.Add(platformListener);
                }
            }
        }

        private JsonTracer(List<EventListener> listeners, Guid parentActivityId, string activityName, EventLevel startStopLevel, Keywords startStopKeywords)
        {
            this.listeners = listeners;
            this.parentActivityId = parentActivityId;
            this.activityName = activityName;
            this.startStopLevel = startStopLevel;
            this.startStopKeywords = startStopKeywords;

            this.activityId = Guid.NewGuid();
        }

        public bool HasLogFileEventListener
        {
            get
            {
                return this.listeners.Any(listener => listener is LogFileEventListener);
            }
        }

        public void AddInProcEventListener(EventListener listener)
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
                foreach (EventListener listener in this.listeners)
                {
                    listener.Dispose();
                }

                this.listeners.Clear();
            }

            this.disposed = true;
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata)
        {
            this.RelatedEvent(level, eventName, metadata, Keywords.None);
        }

        public virtual void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keyword)
        {
            this.WriteEvent(eventName, level, keyword, metadata, opcode: 0);
        }

        public virtual void RelatedInfo(string format, params object[] args)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add(TracingConstants.MessageKey.InfoMessage, string.Format(format, args));
            this.RelatedEvent(EventLevel.Informational, "Information", metadata);
        }

        public virtual void RelatedWarning(EventMetadata metadata, string message)
        {
            this.RelatedWarning(metadata, message, Keywords.None);
        }

        public virtual void RelatedWarning(EventMetadata metadata, string message, Keywords keywords)
        {
            metadata = metadata ?? new EventMetadata();
            metadata[TracingConstants.MessageKey.WarningMessage] = message;
            this.RelatedEvent(EventLevel.Warning, "Warning", metadata, keywords);
        }

        public virtual void RelatedWarning(string message)
        {
            EventMetadata metadata = new EventMetadata();
            this.RelatedWarning(metadata, message);
        }

        public virtual void RelatedWarning(string format, params object[] args)
        {
            this.RelatedWarning(string.Format(format, args));
        }

        public virtual void RelatedError(EventMetadata metadata, string message)
        {
            this.RelatedError(metadata, message, Keywords.Telemetry);
        }

        public virtual void RelatedError(EventMetadata metadata, string message, Keywords keywords)
        {
            metadata = metadata ?? new EventMetadata();
            metadata[TracingConstants.MessageKey.ErrorMessage] = message;
            this.RelatedEvent(EventLevel.Error, GetCategorizedErrorEventName(keywords), metadata, keywords | Keywords.Telemetry);
        }

        public virtual void RelatedError(string message)
        {
            EventMetadata metadata = new EventMetadata();
            this.RelatedError(metadata, message);
        }

        public virtual void RelatedError(string format, params object[] args)
        {
            this.RelatedError(string.Format(format, args));
        }

        public TimeSpan Stop(EventMetadata metadata)
        {
            if (this.stopped)
            {
                return TimeSpan.Zero;
            }

            this.duration.Stop();
            this.stopped = true;

            metadata = metadata ?? new EventMetadata();
            metadata.Add("DurationMs", this.duration.ElapsedMilliseconds);

            this.WriteEvent(this.activityName, this.startStopLevel, this.startStopKeywords, metadata, EventOpcode.Stop);

            return this.duration.Elapsed;
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
            JsonTracer subTracer = new JsonTracer(this.listeners, this.activityId, childActivityName, startStopLevel, startStopKeywords);
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

            if (this.disposed)
            {
                Console.WriteLine("Writing to disposed tracer");
                Console.WriteLine(jsonPayload);

                throw new ObjectDisposedException(nameof(JsonTracer));
            }

            foreach (EventListener listener in this.listeners)
            {
                listener.RecordMessage(eventName, this.activityId, this.parentActivityId, level, keywords, opcode, jsonPayload);
            }
        }
    }
}