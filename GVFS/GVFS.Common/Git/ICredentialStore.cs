using GVFS.Common.Tracing;

namespace GVFS.Common.Git
{
    public interface ICredentialStore
    {
        bool TryGetCredential(ITracer tracer, string url, out string username, out string password, out string error, int timeoutMs = -1);

        bool TryStoreCredential(ITracer tracer, string url, string username, string password, out string error);

        bool TryDeleteCredential(ITracer tracer, string url, string username, string password, out string error);
    }
}
