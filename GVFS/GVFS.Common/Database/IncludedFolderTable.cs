using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace GVFS.Common.Database
{
    public class IncludedFolderTable : IIncludedFolderCollection
    {
        private IGVFSConnectionPool connectionPool;

        public IncludedFolderTable(IGVFSConnectionPool connectionPool)
        {
            this.connectionPool = connectionPool;
        }

        public static void CreateTable(IDbConnection connection)
        {
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE IF NOT EXISTS [IncludedFolder] (path TEXT PRIMARY KEY COLLATE NOCASE) WITHOUT ROWID;";
                command.ExecuteNonQuery();
            }
        }

        public void Add(string directory)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT OR REPLACE INTO IncludedFolder (path) VALUES (@path);";
                    command.AddParameter("@path", DbType.String, directory);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(IncludedFolderTable)}.{nameof(this.Add)}({directory}) Exception: {ex.ToString()}", ex);
            }
        }

        public List<string> GetAll()
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    List<string> directories = new List<string>();
                    command.CommandText = $"SELECT path FROM IncludedFolder;";
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            directories.Add(reader.GetString(0));
                        }
                    }

                    return directories;
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.GetAll)} Exception: {ex.ToString()}", ex);
            }
        }

        public void Remove(string directory)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM IncludedFolder WHERE path = @path;";
                    command.AddParameter("@path", DbType.String, directory);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(IncludedFolderTable)}.{nameof(this.Remove)}({directory}) Exception: {ex.ToString()}", ex);
            }
        }
    }
}
