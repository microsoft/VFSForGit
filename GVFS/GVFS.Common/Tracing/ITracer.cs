using System;

namespace GVFS.Common.Tracing
{
    public interface ITracer : IDisposable
    {
        ITracer StartActivity(string activityName, EventLevel level);

        ITracer StartActivity(string activityName, EventLevel level, EventMetadata metadata);
        ITracer StartActivity(string activityName, EventLevel level, Keywords startStopKeywords, EventMetadata metadata);

        void SetGitCommandSessionId(string sessionId);

        void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata);

        void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keywords);

        void RelatedInfo(string message);

        void RelatedInfo(string format, params object[] args);

        void RelatedInfo(EventMetadata metadata, string message);

        void RelatedWarning(EventMetadata metadata, string message);

        void RelatedWarning(EventMetadata metadata, string message, Keywords keywords);

        void RelatedWarning(string message);

        void RelatedWarning(string format, params object[] args);

        void RelatedError(EventMetadata metadata, string message);

        void RelatedError(EventMetadata metadata, string message, Keywords keywords);

        void RelatedError(string message);

        void RelatedError(string format, params object[] args);

        TimeSpan Stop(EventMetadata metadata);
    }
}