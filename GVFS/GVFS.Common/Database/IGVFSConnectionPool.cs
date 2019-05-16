using System.Data;

namespace GVFS.Common.Database
{
    public interface IGVFSConnectionPool
    {
        IPooledConnection GetConnection();
        void ReturnToPool(IDbConnection connection);
    }
}
