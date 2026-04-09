namespace GVFS.Common.Tracing
{
    public interface IEventListenerEventSink
    {
        void OnListenerRecovery(EventListener listener);

        void OnListenerFailure(EventListener listener, string errorMessage);
    }
}
