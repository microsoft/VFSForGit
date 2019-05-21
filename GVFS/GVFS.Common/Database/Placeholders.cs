using System;
using System.Collections.Generic;
using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// This class is for interacting with the placeholders table in the SQLite database
    /// </summary>
    public class Placeholders : IPlaceholderCollection
    {
        private IGVFSConnectionPool connectionPool;

        public Placeholders(IGVFSConnectionPool connectionPool)
        {
            this.connectionPool = connectionPool;
        }

        public static void CreateTable(IDbCommand command)
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS [Placeholders] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;";
            command.ExecuteNonQuery();
        }

        public int Count()
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                command.CommandText = "SELECT count(path) FROM Placeholders;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            filePlaceholders = new List<IPlaceholderData>();
            folderPlaceholders = new List<IPlaceholderData>();
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                command.CommandText = "SELECT path, pathType, sha FROM Placeholders;";
                using (IDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        PlaceholderData data = new PlaceholderData();
                        data.Path = reader.GetString(0);
                        data.PathType = (PlaceholderData.PlaceholderType)reader.GetByte(1);

                        if (!reader.IsDBNull(2))
                        {
                            data.Sha = reader.GetString(2);
                        }

                        if (data.PathType == PlaceholderData.PlaceholderType.File)
                        {
                            filePlaceholders.Add(data);
                        }
                        else
                        {
                            folderPlaceholders.Add(data);
                        }
                    }
                }
            }
        }

        public HashSet<string> GetAllFilePaths()
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                HashSet<string> fileEntries = new HashSet<string>();
                command.CommandText = "SELECT path FROM Placeholders WHERE pathType = 0;";
                using (IDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fileEntries.Add(reader.GetString(0));
                    }
                }

                return fileEntries;
            }
        }

        public void AddPlaceholderData(IPlaceholderData data)
        {
            if (data.IsFolder)
            {
                if (data.IsExpandedFolder)
                {
                    this.AddExpandedFolder(data.Path);
                }
                else if (data.IsPossibleTombstoneFolder)
                {
                    this.AddPossibleTombstoneFolder(data.Path);
                }
                else
                {
                    this.AddPartialFolder(data.Path);
                }
            }
            else
            {
                this.AddFile(data.Path, data.Sha);
            }
        }

        public void AddFile(string path, string sha)
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.File, Sha = sha });
            }
        }

        public void AddPartialFolder(string path)
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PartialFolder });
            }
        }

        public void AddExpandedFolder(string path)
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.ExpandedFolder });
            }
        }

        public void AddPossibleTombstoneFolder(string path)
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PossibleTombstoneFolder });
            }
        }

        public void Remove(string path)
        {
            using (IPooledConnection pooled = this.connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                Delete(command, path);
            }
        }

        private static void Insert(IDbCommand command, PlaceholderData placeholder)
        {
            command.CommandText = "INSERT OR REPLACE INTO Placeholders (path, pathType, sha) VALUES (@path, @pathType, @sha);";
            command.AddParameter("@path", DbType.String, placeholder.Path);
            command.AddParameter("@pathType", DbType.Int32, (int)placeholder.PathType);
            command.AddParameter("@sha", DbType.String, placeholder.Sha);

            command.ExecuteNonQuery();
        }

        private static void Delete(IDbCommand command, string path)
        {
            command.CommandText = "DELETE FROM Placeholders WHERE path = @path;";
            command.AddParameter("@path", DbType.String, path);
            command.ExecuteNonQuery();
        }

        public class PlaceholderData : IPlaceholderData
        {
            public enum PlaceholderType : byte
            {
                File = 0,
                PartialFolder = 1,
                ExpandedFolder = 2,
                PossibleTombstoneFolder = 3,
            }

            public string Path { get; set; }
            public PlaceholderType PathType { get; set; }
            public string Sha { get; set; }

            public bool IsFolder => this.PathType != PlaceholderType.File;

            public bool IsExpandedFolder => this.PathType == PlaceholderType.ExpandedFolder;

            public bool IsPossibleTombstoneFolder => this.PathType == PlaceholderType.PossibleTombstoneFolder;
        }
    }
}