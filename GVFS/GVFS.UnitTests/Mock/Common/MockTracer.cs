using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockTracer : ITracer
    {
        public void Dispose()
        {
        }

        public void RelatedEvent(EventLevel error, string eventName, EventMetadata metadata)
        {
        }

        public void RelatedEvent(EventLevel error, string eventName, EventMetadata metadata, Keywords keyword)
        {
        }

        public void RelatedError(EventMetadata metadata)
        {
        }

        public void RelatedError(EventMetadata metadata, Keywords keyword)
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

        public ITracer StartActivity(string activityName, EventLevel level, Keywords keyword)
        {
            return new MockTracer();
        }

        public ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata)
        {
            return new MockTracer();
        }

        public ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata, Keywords keyword)
        {
            return new MockTracer();
        }

        public void Stop()
        {
        }

        public void Stop(EventMetadata metadata)
        {
        }
    }
}