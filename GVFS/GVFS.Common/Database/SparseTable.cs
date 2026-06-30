using System.Collections.Generic;
using System.Data;

namespace GVFS.Common.Database
{
    public class SparseTable : GVFSTable, ISparseCollection
    {
        public SparseTable(IGVFSConnectionPool connectionPool)
            : base(connectionPool)
        {
        }

        protected override string TableName => nameof(SparseTable);

        public static void CreateTable(IDbConnection connection, bool caseSensitiveFileSystem)
        {
            using (IDbCommand command = connection.CreateCommand())
            {
                string collateConstraint = caseSensitiveFileSystem ? string.Empty : " COLLATE NOCASE";
                command.CommandText = $"CREATE TABLE IF NOT EXISTS [Sparse] (path TEXT PRIMARY KEY{collateConstraint}) WITHOUT ROWID;";
                command.ExecuteNonQuery();
            }
        }

        public void Add(string directoryPath)
        {
            this.ExecuteWrite(command =>
            {
                command.CommandText = "INSERT OR REPLACE INTO Sparse (path) VALUES (@path);";
                command.AddParameter("@path", DbType.String, GVFSDatabase.NormalizePath(directoryPath));
                command.ExecuteNonQuery();
            });
        }

        public HashSet<string> GetAll()
        {
            return this.ExecuteRead(command =>
            {
                HashSet<string> directories = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);
                command.CommandText = $"SELECT path FROM Sparse;";
                using (IDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        directories.Add(reader.GetString(0));
                    }
                }

                return directories;
            });
        }

        public void Remove(string directoryPath)
        {
            this.ExecuteWrite(command =>
            {
                command.CommandText = "DELETE FROM Sparse WHERE path = @path;";
                command.AddParameter("@path", DbType.String, GVFSDatabase.NormalizePath(directoryPath));
                command.ExecuteNonQuery();
            });
        }
    }
}
