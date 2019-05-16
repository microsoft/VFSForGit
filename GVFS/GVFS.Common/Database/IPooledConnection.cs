using System;
using System.Data;

namespace GVFS.Common.Database
{
    public interface IPooledConnection : IDisposable
    {
        IDbConnection Connection { get; }
    }
}
