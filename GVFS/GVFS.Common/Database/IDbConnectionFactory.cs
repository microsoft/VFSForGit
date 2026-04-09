using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface used to open a new connection to a database
    /// </summary>
    public interface IDbConnectionFactory
    {
        IDbConnection OpenNewConnection(string databasePath);
    }
}
