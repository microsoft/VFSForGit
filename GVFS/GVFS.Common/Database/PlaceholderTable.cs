using System;
using System.Collections.Generic;
using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// This class is for interacting with the Placeholder table in the SQLite database
    /// </summary>
    public class PlaceholderTable : IPlaceholderCollection
    {
        private IGVFSConnectionPool connectionPool;

        public PlaceholderTable(IGVFSConnectionPool connectionPool)
        {
            this.connectionPool = connectionPool;
        }

        public static void CreateTable(IDbConnection connection)
        {
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE IF NOT EXISTS [Placeholder] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;";
                command.ExecuteNonQuery();
            }
        }

        public int GetCount()
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT count(path) FROM Placeholder;";
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.GetCount)} Exception", ex);
            }
        }

        public void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            try
            {
                filePlaceholders = new List<IPlaceholderData>();
                folderPlaceholders = new List<IPlaceholderData>();
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT path, pathType, sha FROM Placeholder;";
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
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.GetAllEntries)} Exception", ex);
            }
        }

        public HashSet<string> GetAllFilePaths()
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    HashSet<string> fileEntries = new HashSet<string>();
                    command.CommandText = $"SELECT path FROM Placeholder WHERE pathType = {(int)PlaceholderData.PlaceholderType.File};";
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
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.GetAllFilePaths)} Exception", ex);
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
            if (sha == null || sha.Length != 40)
            {
                throw new GVFSDatabaseException($"Invalid SHA '{sha ?? "null"}' for file {path}", innerException: null);
            }

            this.Insert(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.File, Sha = sha });
        }

        public void AddPartialFolder(string path)
        {
            this.Insert(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PartialFolder });
        }

        public void AddExpandedFolder(string path)
        {
            this.Insert(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.ExpandedFolder });
        }

        public void AddPossibleTombstoneFolder(string path)
        {
            this.Insert(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PossibleTombstoneFolder });
        }

        public void Remove(string path)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM Placeholder WHERE path = @path;";
                    command.AddParameter("@path", DbType.String, path);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.Remove)}({path}) Exception", ex);
            }
        }

        private void Insert(PlaceholderData placeholder)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT OR REPLACE INTO Placeholder (path, pathType, sha) VALUES (@path, @pathType, @sha);";
                    command.AddParameter("@path", DbType.String, placeholder.Path);
                    command.AddParameter("@pathType", DbType.Int32, (int)placeholder.PathType);

                    if (placeholder.Sha == null)
                    {
                        command.AddParameter("@sha", DbType.String, DBNull.Value);
                    }
                    else
                    {
                        command.AddParameter("@sha", DbType.String, placeholder.Sha);
                    }

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(PlaceholderTable)}.{nameof(this.Insert)}({placeholder.Path}, {placeholder.PathType}, {placeholder.Sha}) Exception", ex);
            }
        }

        public class PlaceholderData : IPlaceholderData
        {
            public enum PlaceholderType
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