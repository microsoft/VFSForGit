using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;

namespace GVFS.Common.Database
{
    /// <summary>
    /// This class is for interacting with the Placeholder table in the SQLite database
    /// </summary>
    public class PlaceholderTable : GVFSTable, IPlaceholderCollection
    {
        public PlaceholderTable(IGVFSConnectionPool connectionPool)
            : base(connectionPool)
        {
        }

        protected override string TableName => nameof(PlaceholderTable);

        public static void CreateTable(IDbConnection connection, bool caseSensitiveFileSystem)
        {
            using (IDbCommand command = connection.CreateCommand())
            {
                string collateConstraint = caseSensitiveFileSystem ? string.Empty : " COLLATE NOCASE";
                command.CommandText = $"CREATE TABLE IF NOT EXISTS [Placeholder] (path TEXT PRIMARY KEY{collateConstraint}, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;";
                command.ExecuteNonQuery();
            }
        }

        public int GetCount()
        {
            return this.ExecuteNonCriticalRead(
                command =>
                {
                    command.CommandText = "SELECT count(path) FROM Placeholder;";
                    return Convert.ToInt32(command.ExecuteScalar());
                },
                fallbackValue: -1);
        }

        public void GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders)
        {
            List<IPlaceholderData> tempFilePlaceholders = new List<IPlaceholderData>();
            List<IPlaceholderData> tempFolderPlaceholders = new List<IPlaceholderData>();

            this.ExecuteRead<object>(command =>
            {
                tempFilePlaceholders.Clear();
                tempFolderPlaceholders.Clear();

                command.CommandText = "SELECT path, pathType, sha FROM Placeholder;";
                ReadPlaceholders(command, data =>
                {
                    if (data.PathType == PlaceholderData.PlaceholderType.File)
                    {
                        tempFilePlaceholders.Add(data);
                    }
                    else
                    {
                        tempFolderPlaceholders.Add(data);
                    }
                });

                return null;
            });

            filePlaceholders = tempFilePlaceholders;
            folderPlaceholders = tempFolderPlaceholders;
        }

        public HashSet<string> GetAllFilePaths()
        {
            return this.ExecuteRead(command =>
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
            });
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
                    this.AddPartialFolder(data.Path, data.Sha);
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

            this.InsertPlaceholder(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.File, Sha = sha });
        }

        public void AddPartialFolder(string path, string sha)
        {
            this.InsertPlaceholder(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PartialFolder, Sha = sha });
        }

        public void AddExpandedFolder(string path)
        {
            this.InsertPlaceholder(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.ExpandedFolder });
        }

        public void AddPossibleTombstoneFolder(string path)
        {
            this.InsertPlaceholder(new PlaceholderData() { Path = path, PathType = PlaceholderData.PlaceholderType.PossibleTombstoneFolder });
        }

        public List<IPlaceholderData> RemoveAllEntriesForFolder(string path)
        {
            const string fromWhereClause = "FROM Placeholder WHERE path = @path OR path LIKE @pathWithDirectorySeparator;";

            path = GVFSDatabase.NormalizePath(path);

            return this.ExecuteReadThenWrite(command =>
            {
                List<IPlaceholderData> removedPlaceholders = new List<IPlaceholderData>();
                command.CommandText = $"SELECT path, pathType, sha {fromWhereClause}";
                command.AddParameter("@path", DbType.String, $"{path}");
                command.AddParameter("@pathWithDirectorySeparator", DbType.String, $"{path + Path.DirectorySeparatorChar}%");
                ReadPlaceholders(command, data => removedPlaceholders.Add(data));

                command.CommandText = $"DELETE {fromWhereClause}";

                lock (this.WriterLock)
                {
                    command.ExecuteNonQuery();
                }

                return removedPlaceholders;
            });
        }

        public void Remove(string path)
        {
            this.ExecuteWrite(command =>
            {
                command.CommandText = "DELETE FROM Placeholder WHERE path = @path;";
                command.AddParameter("@path", DbType.String, path);
                command.ExecuteNonQuery();
            });
        }

        public int GetFilePlaceholdersCount()
        {
            return this.ExecuteNonCriticalRead(
                command =>
                {
                    command.CommandText = $"SELECT count(path) FROM Placeholder WHERE pathType = {(int)PlaceholderData.PlaceholderType.File};";
                    return Convert.ToInt32(command.ExecuteScalar());
                },
                fallbackValue: -1);
        }

        public int GetFolderPlaceholdersCount()
        {
            return this.ExecuteNonCriticalRead(
                command =>
                {
                    command.CommandText = $"SELECT count(path) FROM Placeholder WHERE pathType = {(int)PlaceholderData.PlaceholderType.PartialFolder};";
                    return Convert.ToInt32(command.ExecuteScalar());
                },
                fallbackValue: -1);
        }

        private static void ReadPlaceholders(IDbCommand command, Action<PlaceholderData> dataHandler)
        {
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

                    dataHandler(data);
                }
            }
        }

        private void InsertPlaceholder(PlaceholderData placeholder, [CallerMemberName] string caller = null)
        {
            try
            {
                this.ExecuteWrite(command =>
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
                }, caller);
            }
            catch (GVFSDatabaseException ex)
            {
                throw new GVFSDatabaseException(
                    $"{this.TableName}.{caller}({placeholder.Path}, {placeholder.PathType}, {placeholder.Sha ?? "null"}) Exception",
                    ex.InnerException);
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

