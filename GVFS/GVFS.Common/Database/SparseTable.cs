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
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT OR REPLACE INTO Sparse (path) VALUES (@path);";
                    command.AddParameter("@path", DbType.String, GVFSDatabase.NormalizePath(directoryPath));

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
                    command.AddParameter("@path", DbType.String, GVFSDatabase.NormalizePath(directoryPath));

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
