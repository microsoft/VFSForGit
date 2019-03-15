using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Tracing
{
    public class JsonTracer : ITracer, IEventListenerEventSink
    {
        public const string NetworkErrorEventName = "NetworkError";

        private readonly ConcurrentBag<EventListener> listeners;
        private readonly ConcurrentDictionary<EventListener, string> failedListeners = new ConcurrentDictionary<EventListener, string>();

        private readonly string activityName;
        private readonly Guid parentActivityId;
        private readonly Guid activityId;
        private readonly Stopwatch duration = Stopwatch.StartNew();

        private readonly EventLevel startStopLevel;
        private readonly Keywords startStopKeywords;

        private bool isDisposed = false;
        private bool stopped = false;

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
                  null,
                  providerActivityId,
                  activityName,
                  EventLevel.Informational,
                  Keywords.Telemetry)
        {
            if (!disableTelemetry)
            {
                string gitBinRoot = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

                // If we do not have a git binary, then we cannot check if we should set up telemetry
                // We also cannot log this, as we are setting up tracer.
                if (string.IsNullOrEmpty(gitBinRoot))
                {
                    return;
                }

                TelemetryDaemonEventListener daemonListener = TelemetryDaemonEventListener.CreateIfEnabled(gitBinRoot, providerName, enlistmentId, mountId, this);
                if (daemonListener != null)
                {
                    this.listeners.Add(daemonListener);
                }
            }
        }

        private JsonTracer(ConcurrentBag<EventListener> listeners, Guid parentActivityId, string activityName, EventLevel startStopLevel, Keywords startStopKeywords)
        {
            this.listeners = listeners ?? new ConcurrentBag<EventListener>();
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

        public void SetGitCommandSessionId(string sessionId)
        {
            TelemetryDaemonEventListener daemonListener = this.listeners.FirstOrDefault(x => x is TelemetryDaemonEventListener) as TelemetryDaemonEventListener;
            if (daemonListener != null)
            {
                daemonListener.GitCommandSessionId = sessionId;
            }
        }

        public void AddEventListener(EventListener listener)
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(JsonTracer));
            }

            this.listeners.Add(listener);

            // Tell the new listener about others who have previously failed
            foreach (KeyValuePair<EventListener, string> kvp in this.failedListeners)
            {
                TraceEventMessage failureMessage = CreateListenerFailureMessage(kvp.Key, kvp.Value);
                listener.RecordMessage(failureMessage);
            }
        }

        public void AddDiagnosticConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.AddEventListener(new DiagnosticConsoleEventListener(maxVerbosity, keywordFilter, this));
        }

        public void AddPrettyConsoleEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.AddEventListener(new PrettyConsoleEventListener(maxVerbosity, keywordFilter, this));
        }

        public void AddLogFileEventListener(string logFilePath, EventLevel maxVerbosity, Keywords keywordFilter)
        {
            this.AddEventListener(new LogFileEventListener(logFilePath, maxVerbosity, keywordFilter, this));
        }

        public void Dispose()
        {
            if (this.isDisposed)
            {
                // This instance has already been disposed
                return;
            }

            this.Stop(null);

            // If we have no parent, then we are the root tracer and should dispose our eventsource.
            if (this.parentActivityId == Guid.Empty)
            {
                // Empty the listener bag and dispose of the instances as we remove them.
                EventListener listener;
                while (this.listeners.TryTake(out listener))
                {
                    listener.Dispose();
                }
            }

            this.isDisposed = true;
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
            this.RelatedInfo(string.Format(format, args));
        }

        public virtual void RelatedInfo(string message)
        {
            this.RelatedInfo(new EventMetadata(), message);
        }

        public virtual void RelatedInfo(EventMetadata metadata, string message)
        {
            metadata = metadata ?? new EventMetadata();
            metadata.Add(TracingConstants.MessageKey.InfoMessage, message);
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

            // Write the start event, disabling the Telemetry keyword so we will only dispatch telemetry at the end event.
            subTracer.WriteStartEvent(startMetadata, startStopKeywords & ~Keywords.Telemetry);

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

        void IEventListenerEventSink.OnListenerRecovery(EventListener listener)
        {
            // Check ContainsKey first (rather than always calling TryRemove) because ContainsKey
            // is lock-free and recoveredListener should rarely be in failedListeners
            if (!this.failedListeners.ContainsKey(listener))
            {
                // This listener has not failed since the last time it was called, so no need to log recovery
                return;
            }

            if (this.failedListeners.TryRemove(listener, out _))
            {
                TraceEventMessage message = CreateListenerRecoveryMessage(listener);
                this.LogMessageToNonFailedListeners(message);
            }
        }

        void IEventListenerEventSink.OnListenerFailure(EventListener listener, string errorMessage)
        {
            if (!this.failedListeners.TryAdd(listener, errorMessage))
            {
                // We've already logged that this listener has failed so there is no need to do it again
                return;
            }

            TraceEventMessage message = CreateListenerFailureMessage(listener, errorMessage);
            this.LogMessageToNonFailedListeners(message);
        }

        private static string GetCategorizedErrorEventName(Keywords keywords)
        {
            switch (keywords)
            {
                case Keywords.Network: return NetworkErrorEventName;
                default: return "Error";
            }
        }

        private static TraceEventMessage CreateListenerRecoveryMessage(EventListener recoveredListener)
        {
            return new TraceEventMessage
            {
                EventName = "TraceEventListenerRecovery",
                Level = EventLevel.Informational,
                Keywords = Keywords.Any,
                Opcode = EventOpcode.Info,
                Payload = JsonConvert.SerializeObject(new Dictionary<string, string>
                {
                    ["EventListener"] = recoveredListener.GetType().Name
                })
            };
        }

        private static TraceEventMessage CreateListenerFailureMessage(EventListener failedListener, string errorMessage)
        {
            return new TraceEventMessage
            {
                EventName = "TraceEventListenerFailure",
                Level = EventLevel.Error,
                Keywords = Keywords.Any,
                Opcode = EventOpcode.Info,
                Payload = JsonConvert.SerializeObject(new Dictionary<string, string>
                {
                    ["EventListener"] = failedListener.GetType().Name,
                    ["ErrorMessage"] = errorMessage,
                })
            };
        }

        private void WriteEvent(string eventName, EventLevel level, Keywords keywords, EventMetadata metadata, EventOpcode opcode)
        {
            string jsonPayload = metadata != null ? JsonConvert.SerializeObject(metadata) : null;

            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(JsonTracer));
            }

            var message = new TraceEventMessage
            {
                EventName = eventName,
                ActivityId = this.activityId,
                ParentActivityId = this.parentActivityId,
                Level = level,
                Keywords = keywords,
                Opcode = opcode,
                Payload = jsonPayload
            };

            // Iterating over the bag is thread-safe as the enumerator returned here
            // is of a snapshot of the bag.
            foreach (EventListener listener in this.listeners)
            {
                listener.RecordMessage(message);
            }
        }

        private void LogMessageToNonFailedListeners(TraceEventMessage message)
        {
            foreach (EventListener listener in this.listeners.Except(this.failedListeners.Keys))
            {
                // To prevent infinitely recursive failures, we won't try and log that we failed to log that a listener failed :)
                listener.RecordMessage(message);
            }
        }
    }
}
