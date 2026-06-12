using System;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Strategy interface for checking whether git objects exist locally.
    /// Implementations must be safe to call from a single worker thread.
    /// Thread-safety across multiple workers depends on the implementation.
    /// </summary>
    public interface IObjectExistenceChecker : IDisposable
    {
        bool ObjectExists(string sha);
    }
}
