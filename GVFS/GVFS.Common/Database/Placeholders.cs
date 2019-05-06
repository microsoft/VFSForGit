using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace GVFS.Common.Database
{
    public class Placeholders : IPlaceholderDatabase
    {
        private SqliteConnection connection;

        public Placeholders(SqliteConnection connection)
        {
            this.connection = connection;
        }

        public int Count
        {
            get
            {
                using (SqliteCommand command = this.connection.CreateCommand())
                {
                    command.CommandText = $"SELECT count(path) FROM Placeholders;";
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public static void CreateTable(SqliteCommand command)
        {
            command.CommandText = @"CREATE TABLE IF NOT EXISTS [Placeholders] (path TEXT PRIMARY KEY, pathType TINYINT DEFAULT 0, sha char(40) ) WITHOUT ROWID;";
            command.ExecuteNonQuery();
        }

        public void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            filePlaceholders = new List<IPlaceholderData>();
            folderPlaceholders = new List<IPlaceholderData>();
            using (SqliteCommand command = this.connection.CreateCommand())
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
            using (SqliteCommand command = this.connection.CreateCommand())
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

        public bool Contains(string path)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                command.Parameters.AddWithValue("@path", path);
                command.CommandText = $"SELECT 1 FROM Placeholders WHERE path = @path;";
                object result = command.ExecuteScalar();
                return result != DBNull.Value;
            }
        }

        public void AddFile(string path, string sha)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.File, Sha = sha });
            }
        }

        public void AddPartialFolder(string path)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PartialFolder });
            }
        }

        public void AddExpandedFolder(string path)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.ExpandedFolder });
            }
        }

        public void AddPossibleTombstoneFolder(string path)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                Insert(command, new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PossibleTombstoneFolder });
            }
        }

        public void Remove(string path)
        {
            using (SqliteCommand command = this.connection.CreateCommand())
            {
                Delete(command, path);
            }
        }

        private static void Insert(SqliteCommand command, PlaceholderData placeholder)
        {
            command.Parameters.AddWithValue("@path", placeholder.Path);
            command.Parameters.AddWithValue("@pathType", placeholder.PathType);

            if (placeholder.Sha == null)
            {
                command.Parameters.AddWithValue("@sha", DBNull.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@sha", placeholder.Sha);
            }

            command.CommandText = $"INSERT OR REPLACE INTO Placeholders (path, pathType, sha) VALUES (@path, @pathType, @sha);";
            command.ExecuteNonQuery();
        }

        private static void Delete(SqliteCommand command, string path)
        {
            command.Parameters.AddWithValue("@path", path);
            command.CommandText = $"DELETE FROM Placeholders WHERE path = @path;";
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