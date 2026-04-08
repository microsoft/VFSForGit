using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Tracing
{
    /// <summary>
    /// Empty implementation of ITracer that does nothing
    /// </summary>
    public sealed class NullTracer : ITracer
    {
        private NullTracer()
        {
        }

        public static ITracer Instance { get; } = new NullTracer();

        void IDisposable.Dispose()
        {

        }

        void ITracer.RelatedError(EventMetadata metadata, string message)
        {

        }

        void ITracer.RelatedError(EventMetadata metadata, string message, Keywords keywords)
        {

        }

        void ITracer.RelatedError(string message)
        {

        }

        void ITracer.RelatedError(string format, params object[] args)
        {

        }

        void ITracer.RelatedEvent(EventLevel level, string eventName, EventMetadata metadata)
        {

        }

        void ITracer.RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keywords)
        {

        }

        void ITracer.RelatedInfo(string message)
        {

        }

        void ITracer.RelatedInfo(string format, params object[] args)
        {

        }

        void ITracer.RelatedInfo(EventMetadata metadata, string message)
        {

        }

        void ITracer.RelatedWarning(EventMetadata metadata, string message)
        {

        }

        void ITracer.RelatedWarning(EventMetadata metadata, string message, Keywords keywords)
        {

        }

        void ITracer.RelatedWarning(string message)
        {

        }

        void ITracer.RelatedWarning(string format, params object[] args)
        {

        }

        void ITracer.SetGitCommandSessionId(string sessionId)
        {

        }

        ITracer ITracer. StartActivity(string activityName, EventLevel level)
        {
            return this;
        }

        ITracer ITracer. StartActivity(string activityName, EventLevel level, EventMetadata metadata)
        {
            return this;
        }

        ITracer ITracer. StartActivity(string activityName, EventLevel level, Keywords startStopKeywords, EventMetadata metadata)
        {
            return this;
        }

        TimeSpan ITracer.Stop(EventMetadata metadata)
        {
            return TimeSpan.Zero;
        }
    }
}
