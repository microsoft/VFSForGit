using System;

namespace GVFS.Common.Tracing
{
    public interface IQueuedPipeStringWriterEventSink
    {
        void OnStateChanged(QueuedPipeStringWriter writer, QueuedPipeStringWriterState state, Exception exception);
    }
}
