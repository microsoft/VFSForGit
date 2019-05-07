using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.IO;

namespace GVFS.Common.Database
{
    public class GVFSDatabase : IDisposable
    {
        private string databasePath;
        private string sqliteConnectionString;

        public GVFSDatabase(GVFSContext context)
        {
            this.databasePath = Path.Combine(context.Enlistment.EnlistmentRoot, GVFSConstants.DotGVFS.Root, GVFSConstants.DotGVFS.Databases.GVFSDatabase);
            this.sqliteConnectionString = $"data source={this.databasePath};Cache=Shared;";

            string folderPath = Path.GetDirectoryName(this.databasePath);
            context.FileSystem.CreateDirectory(folderPath);

            this.Connection = new SqliteConnection(this.sqliteConnectionString);
            this.Connection.Open();
            this.Initialize();
            this.CreateTables();
        }

        public SqliteConnection Connection { get; }

        public void Dispose()
        {
            this.Connection?.Dispose();
        }

        private void Initialize()
        {
            using (SqliteCommand command = this.Connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
                command.CommandText = $"PRAGMA cache_size=-40000;";
                command.ExecuteNonQuery();
                command.CommandText = $"PRAGMA synchronous=NORMAL;";
                command.ExecuteNonQuery();
                command.CommandText = $"PRAGMA user_version;";
                object userVersion = command.ExecuteScalar();
                if (userVersion == null || Convert.ToInt64(userVersion) < 1)
                {
                    command.CommandText = $"PRAGMA user_version=1;";
                    command.ExecuteNonQuery();
                }
            }
        }

        private void CreateTables()
        {
            using (SqliteCommand command = this.Connection.CreateCommand())
            {
                Placeholders.CreateTable(command);
            }
        }
    }
}
