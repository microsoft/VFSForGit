using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
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
        }

        public string WaitRelatedEventName { get; set; }

        public List<string> RelatedInfoEvents { get; }
        public List<string> RelatedWarningEvents { get; }

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

        public void RelatedInfo(string format, params object[] args)
        {
            this.RelatedInfoEvents.Add(string.Format(format, args));
        }
        
        public void RelatedWarning(EventMetadata metadata, string message)
        {
            metadata[TracingConstants.MessageKey.WarningMessage] = message;
            this.RelatedWarningEvents.Add(JsonConvert.SerializeObject(metadata));
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
        }

        public void RelatedError(EventMetadata metadata, string message, Keywords keyword)
        {
        }

        public void RelatedError(string message)
        {
        }

        public void RelatedError(string format, params object[] args)
        {
        }

        public ITracer StartActivity(string activityName, EventLevel level)
        {
            return new MockTracer();
        }

        public ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata)
        {
            return new MockTracer();
        }

        public ITracer StartActivity(string activityName, EventLevel level, Keywords startStopKeywords, EventMetadata metadata)
        {
            return new MockTracer();
        }

        public TimeSpan Stop(EventMetadata metadata)
        {
            return TimeSpan.Zero;
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