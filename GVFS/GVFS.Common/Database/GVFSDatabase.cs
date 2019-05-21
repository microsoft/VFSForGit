using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Handles setting up the database for storing data used by GVFS and
    /// managing the connections to the database
    /// </summary>
    public class GVFSDatabase : IGVFSConnectionPool, IDisposable
    {
        private const int InitialPooledConnections = 5;
        private const int MillisecondsWaitingToGetConnection = 50;

        private bool disposed = false;
        private ITracer tracer;
        private string databasePath;
        private IDbConnectionCreator connectionCreator;
        private BlockingCollection<IDbConnection> connectionPool;

        public GVFSDatabase(ITracer tracer, PhysicalFileSystem fileSystem, string enlistmentRoot, IDbConnectionCreator connectionCreator, int initialPooledConnections = InitialPooledConnections)
        {
            this.tracer = tracer;
            this.connectionPool = new BlockingCollection<IDbConnection>();
            this.databasePath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root, GVFSConstants.DotGVFS.Databases.VFSForGit);
            this.connectionCreator = connectionCreator;

            string folderPath = Path.GetDirectoryName(this.databasePath);
            fileSystem.CreateDirectory(folderPath);

            for (int i = 0; i < initialPooledConnections; i++)
            {
                this.connectionPool.Add(this.connectionCreator.OpenNewConnection(this.databasePath));
            }

            this.Initialize();
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.connectionPool.CompleteAdding();
            while (!this.connectionPool.IsCompleted && this.connectionPool.TryTake(out IDbConnection connection))
            {
                connection.Dispose();
            }
        }

        IPooledConnection IGVFSConnectionPool.GetConnection()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(GVFSDatabase));
            }

            IDbConnection connection;
            if (!this.connectionPool.TryTake(out connection, millisecondsTimeout: MillisecondsWaitingToGetConnection))
            {
                connection = this.connectionCreator.OpenNewConnection(this.databasePath);
            }

            return new GVFSConnection(this, connection);
        }

        private void ReturnToPool(IDbConnection connection)
        {
            if (this.connectionPool.IsAddingCompleted)
            {
                connection.Dispose();
                return;
            }

            bool itemWasAdded = false;
            try
            {
                itemWasAdded = this.connectionPool.TryAdd(connection);
            }
            catch (InvalidOperationException)
            {
                itemWasAdded = false;
            }

            if (!itemWasAdded)
            {
                connection.Dispose();
            }
        }

        private void Initialize()
        {
            IGVFSConnectionPool connectionPool = this;
            using (IPooledConnection pooled = connectionPool.GetConnection())
            using (IDbCommand command = pooled.Connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA cache_size=-40000;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA synchronous=NORMAL;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA user_version;";
                object userVersion = command.ExecuteScalar();
                if (userVersion == null || Convert.ToInt64(userVersion) < 1)
                {
                    command.CommandText = "PRAGMA user_version=1;";
                    command.ExecuteNonQuery();
                }

                PlaceholdersTable.CreateTable(command);
            }
        }

        private class GVFSConnection : IPooledConnection
        {
            private IDbConnection connection;
            private GVFSDatabase database;

            public GVFSConnection(GVFSDatabase database, IDbConnection connection)
            {
                this.database = database;
                this.connection = connection;
            }

            IDbConnection IPooledConnection.Connection => this.connection;

            public void Dispose()
            {
                this.database.ReturnToPool(this.connection);
            }
        }
    }
}
