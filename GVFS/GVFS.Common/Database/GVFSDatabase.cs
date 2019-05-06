using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace GVFS.Common.Database
{
    public class GVFSDatabase : IDisposable
    {
        private ITracer tracer;
        private string databasePath;
        private string sqliteConnectionString;

        public GVFSDatabase(ITracer tracer, PhysicalFileSystem fileSystem, string enlistmentRoot)
        {
            this.tracer = tracer;
            this.databasePath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root, GVFSConstants.DotGVFS.Databases.GVFSDatabase);
            this.sqliteConnectionString = $"data source={this.databasePath};Cache=Shared";

            string folderPath = Path.GetDirectoryName(this.databasePath);
            fileSystem.CreateDirectory(folderPath);

            bool databaseInitialized = fileSystem.FileExists(this.databasePath);

            this.Connection = new SqliteConnection(this.sqliteConnectionString);
            this.Connection.Open();

            if (!databaseInitialized)
            {
                this.Initialize();
            }
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
                command.CommandText = $"PRAGMA synchronous=FULL;";
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
    }
}
