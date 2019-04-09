using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockTracer : ITracer
    {
        private AutoResetEvent waitEvent;

        public MockTracer()
        {
            this.waitEvent = new AutoResetEvent(false);
            this.RelatedInfoEvents = new List<string>();
            this.RelatedWarningEvents = new List<string>();
            this.RelatedErrorEvents = new List<string>();
        }

        public MockTracer StartActivityTracer { get; private set; }
        public string WaitRelatedEventName { get; set; }

        public List<string> RelatedInfoEvents { get; }
        public List<string> RelatedWarningEvents { get; }
        public List<string> RelatedErrorEvents { get; }

        public void WaitForRelatedEvent()
        {
            this.waitEvent.WaitOne();
        }

        public void RelatedEvent(EventLevel error, string eventName, EventMetadata metadata)
        {
            if (eventName == this.WaitRelatedEventName)
            {
                this.waitEvent.Set();
            }
        }

        public void RelatedEvent(EventLevel error, string eventName, EventMetadata metadata, Keywords keyword)
        {
            if (eventName == this.WaitRelatedEventName)
            {
                this.waitEvent.Set();
            }
        }

        public void RelatedInfo(string message)
        {
            this.RelatedInfoEvents.Add(message);
        }

        public void RelatedInfo(EventMetadata metadata, string message)
        {
            metadata[TracingConstants.MessageKey.InfoMessage] = message;
            this.RelatedInfoEvents.Add(JsonConvert.SerializeObject(metadata));
        }

        public void RelatedInfo(string format, params object[] args)
        {
            this.RelatedInfo(string.Format(format, args));
        }

        public void RelatedWarning(EventMetadata metadata, string message)
        {
            if (metadata != null)
            {
                metadata[TracingConstants.MessageKey.WarningMessage] = message;
                this.RelatedWarningEvents.Add(JsonConvert.SerializeObject(metadata));
            }
            else if (message != null)
            {
                this.RelatedWarning(message);
            }
        }

        public void RelatedWarning(EventMetadata metadata, string message, Keywords keyword)
        {
            this.RelatedWarning(metadata, message);
        }

        public void RelatedWarning(string message)
        {
            this.RelatedWarningEvents.Add(message);
        }

        public void RelatedWarning(string format, params object[] args)
        {
            this.RelatedWarningEvents.Add(string.Format(format, args));
        }

        public void RelatedError(EventMetadata metadata, string message)
        {
            metadata[TracingConstants.MessageKey.ErrorMessage] = message;
            this.RelatedErrorEvents.Add(JsonConvert.SerializeObject(metadata));
        }

        public void RelatedError(EventMetadata metadata, string message, Keywords keyword)
        {
            this.RelatedError(metadata, message);
        }

        public void RelatedError(string message)
        {
            this.RelatedErrorEvents.Add(message);
        }

        public void RelatedError(string format, params object[] args)
        {
            this.RelatedErrorEvents.Add(string.Format(format, args));
        }

        public ITracer StartActivity(string activityName, EventLevel level)
        {
            return this.StartActivity(activityName, level, metadata: null);
        }

        public ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata)
        {
            return this.StartActivity(activityName, level, Keywords.None, metadata);
        }

        public ITracer StartActivity(string activityName, EventLevel level, Keywords startStopKeywords, EventMetadata metadata)
        {
            this.StartActivityTracer = this.StartActivityTracer ?? new MockTracer();
            return this.StartActivityTracer;
        }

        public TimeSpan Stop(EventMetadata metadata)
        {
            return TimeSpan.Zero;
        }

        public void SetGitCommandSessionId(string sessionId)
        {
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.waitEvent != null)
                {
                    this.waitEvent.Dispose();
                    this.waitEvent = null;
                }
            }
        }
    }
}