using GVFS.Common.Tracing;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace GVFS.Common.Database
{
    public class GVFSDatabase : IDisposable
    {
        private const int InitialPooledConnections = 3;

        private bool disposed = false;
        private ITracer tracer;
        private string databasePath;
        private string sqliteConnectionString;
        private BlockingCollection<SqliteConnection> connectionPool;

        public GVFSDatabase(GVFSContext context)
        {
            this.tracer = context.Tracer;
            this.connectionPool = new BlockingCollection<SqliteConnection>();
            this.databasePath = Path.Combine(context.Enlistment.EnlistmentRoot, GVFSConstants.DotGVFS.Root, GVFSConstants.DotGVFS.Databases.GVFSDatabase);
            this.sqliteConnectionString = $"data source={this.databasePath};Cache=Shared;";

            string folderPath = Path.GetDirectoryName(this.databasePath);
            context.FileSystem.CreateDirectory(folderPath);

            for (int i = 0; i < InitialPooledConnections; i++)
            {
                this.connectionPool.Add(this.OpenNewConnection());
            }

            this.Initialize();
            this.CreateTables();
        }

        public interface IPooledConnection : IDisposable
        {
            SqliteConnection Connection { get; }
        }

        public void Dispose()
        {
            this.disposed = true;
            this.connectionPool.CompleteAdding();
            while (!this.connectionPool.IsCompleted && this.connectionPool.TryTake(out SqliteConnection connection))
            {
                connection.Dispose();
            }
        }

        public IPooledConnection GetPooledConnection()
        {
            SqliteConnection connection;
            if (!this.connectionPool.TryTake(out connection, millisecondsTimeout: 500))
            {
                connection = this.OpenNewConnection();
            }

            return new GVFSConnection(this, connection);
        }

        private void Initialize()
        {
            using (IPooledConnection pooled = this.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
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
            using (IPooledConnection pooled = this.GetPooledConnection())
            using (SqliteCommand command = pooled.Connection.CreateCommand())
            {
                Placeholders.CreateTable(command);
            }
        }

        private SqliteConnection OpenNewConnection()
        {
            SqliteConnection connection = new SqliteConnection(this.sqliteConnectionString);
            connection.Open();
            return connection;
        }

        private void ReturnToPool(SqliteConnection connection)
        {
            if (this.disposed)
            {
                connection?.Dispose();
            }
            else if (!this.connectionPool.TryAdd(connection))
            {
                connection?.Dispose();
            }
        }

        private class GVFSConnection : IPooledConnection
        {
            private SqliteConnection connection;
            private GVFSDatabase database;

            public GVFSConnection(GVFSDatabase database, SqliteConnection connection)
            {
                this.database = database;
                this.connection = connection;
            }

            public SqliteConnection Connection => this.connection;

            public void Dispose()
            {
                this.database.ReturnToPool(this.connection);
            }
        }
    }
}
