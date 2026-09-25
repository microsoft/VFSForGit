using GVFS.Common.Tracing;
using System.Threading;

namespace GVFS.Common.Git
{
    public interface ICredentialStore
    {
        bool TryGetCredential(ITracer tracer, string url, out string username, out string password, out string error, out bool timedOut, int timeoutMs = -1, CancellationToken cancellationToken = default);

        bool TryStoreCredential(ITracer tracer, string url, string username, string password, out string error, int timeoutMs = -1, CancellationToken cancellationToken = default);

        bool TryDeleteCredential(ITracer tracer, string url, string username, string password, out string error, int timeoutMs = -1, CancellationToken cancellationToken = default);
    }
}
