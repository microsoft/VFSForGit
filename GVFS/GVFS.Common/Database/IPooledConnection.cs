using System;
using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface for a pooled database connection
    /// </summary>
    public interface IPooledConnection : IDisposable
    {
        IDbConnection Connection { get; }
    }
}
