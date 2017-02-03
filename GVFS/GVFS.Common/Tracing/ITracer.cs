using System;
using Microsoft.Diagnostics.Tracing;

namespace GVFS.Common.Tracing
{
    public interface ITracer : IDisposable
    {
        ITracer StartActivity(string activityName, EventLevel level);

        ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata);
        
        void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata);

        void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keywords);

        void RelatedError(EventMetadata metadata);

        void RelatedError(EventMetadata metadata, Keywords keywords);

        void RelatedError(string message);

        void RelatedError(string format, params object[] args);

        void Stop(EventMetadata metadata);
    }
}