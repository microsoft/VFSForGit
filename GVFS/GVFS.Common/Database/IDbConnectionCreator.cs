using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface used to open a new connection to a database
    /// </summary>
    public interface IDbConnectionCreator
    {
        IDbConnection OpenNewConnection(string databasePath);
    }
}
