using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface for getting a pooled database connection
    /// </summary>
    public interface IGVFSConnectionPool
    {
        IDbConnection GetConnection();
    }
}
