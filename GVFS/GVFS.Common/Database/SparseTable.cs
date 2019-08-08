using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace GVFS.Common.Database
{
    public class SparseTable : ISparseCollection
    {
        private IGVFSConnectionPool connectionPool;
        private object writerLock = new object();

        public SparseTable(IGVFSConnectionPool connectionPool)
        {
            this.connectionPool = connectionPool;
        }

        public static string NormalizePath(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim().Trim(Path.DirectorySeparatorChar);
        }

        public static void CreateTable(IDbConnection connection)
        {
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE IF NOT EXISTS [Sparse] (path TEXT PRIMARY KEY COLLATE NOCASE) WITHOUT ROWID;";
                command.ExecuteNonQuery();
            }
        }

        public void Add(string directoryPath)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT OR REPLACE INTO Sparse (path) VALUES (@path);";
                    command.AddParameter("@path", DbType.String, NormalizePath(directoryPath));

                    lock (this.writerLock)
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(SparseTable)}.{nameof(this.Add)}({directoryPath}) Exception: {ex.ToString()}", ex);
            }
        }

        public HashSet<string> GetAll()
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    command.CommandText = $"SELECT path FROM Sparse;";
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
                throw new GVFSDatabaseException($"{nameof(SparseTable)}.{nameof(this.GetAll)} Exception: {ex.ToString()}", ex);
            }
        }

        public void Remove(string directoryPath)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM Sparse WHERE path = @path;";
                    command.AddParameter("@path", DbType.String, NormalizePath(directoryPath));

                    lock (this.writerLock)
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(SparseTable)}.{nameof(this.Remove)}({directoryPath}) Exception: {ex.ToString()}", ex);
            }
        }
    }
}
