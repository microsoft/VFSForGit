using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace GVFS.Common.Database
{
    public class Placeholders : IPlaceholderDatabase
    {
        private GVFSDatabase database;

        public Placeholders(GVFSDatabase database)
        {
            this.database = database;
        }

        public static void CreateTable(SqliteCommand command)
        {
            command.CommandText = @"CREATE TABLE IF NOT EXISTS [Placeholders] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;";
            command.ExecuteNonQuery();
        }

        public int Count()
        {
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                command.CommandText = $"SELECT count(path) FROM Placeholders;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            filePlaceholders = new List<IPlaceholderData>();
            folderPlaceholders = new List<IPlaceholderData>();
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                command.CommandText = $"SELECT path, pathType, sha FROM Placeholders;";
                using (SqliteDataReader reader = command.ExecuteReader())
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
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                HashSet<string> fileEntries = new HashSet<string>();
                command.CommandText = $"SELECT path FROM Placeholders WHERE pathType = 0;";
                using (SqliteDataReader reader = command.ExecuteReader())
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
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.File, Sha = sha });
            }
        }

        public void AddPartialFolder(string path)
        {
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PartialFolder });
            }
        }

        public void AddExpandedFolder(string path)
        {
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.ExpandedFolder });
            }
        }

        public void AddPossibleTombstoneFolder(string path)
        {
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PossibleTombstoneFolder });
            }
        }

        public void Remove(string path)
        {
            using (GVFSDatabase.IPooledConnection pooled = this.database.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Delete(command, path);
            }
        }

        private static void Insert(SqliteCommand command, PlaceholderData placeholder)
        {
            command.CommandText = $"INSERT OR REPLACE INTO Placeholders (path, pathType, sha) VALUES (@path, @pathType, @sha);";
            command.Parameters.Add("@path", SqliteType.Text).Value = placeholder.Path;
            command.Parameters.Add("@pathType", SqliteType.Integer).Value = (byte)placeholder.PathType;
            if (placeholder.Sha == null)
            {
                command.Parameters.Add("@sha", SqliteType.Text).Value = DBNull.Value;
            }
            else
            {
                command.Parameters.Add("@sha", SqliteType.Text).Value = placeholder.Sha;
            }

            command.ExecuteNonQuery();
        }

        private static void Delete(SqliteCommand command, string path)
        {
            command.CommandText = $"DELETE FROM Placeholders WHERE path = @path;";
            command.Parameters.Add("@path", SqliteType.Text).Value = path;
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