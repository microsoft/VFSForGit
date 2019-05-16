using System.Data;

namespace GVFS.Common.Database
{
    public interface IDbConnectionCreator
    {
        IDbConnection OpenNewConnection(string databasePath);
    }
}
