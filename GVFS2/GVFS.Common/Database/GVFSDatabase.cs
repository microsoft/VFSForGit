using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
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
        private string databasePath;
        private IDbConnectionFactory connectionFactory;
        private BlockingCollection<IDbConnection> connectionPool;

        public GVFSDatabase(PhysicalFileSystem fileSystem, string enlistmentRoot, IDbConnectionFactory connectionFactory, int initialPooledConnections = InitialPooledConnections)
        {
            this.connectionPool = new BlockingCollection<IDbConnection>();
            this.databasePath = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.VFSForGit);
            this.connectionFactory = connectionFactory;

            string folderPath = Path.GetDirectoryName(this.databasePath);
            fileSystem.CreateDirectory(folderPath);

            try
            {
                for (int i = 0; i < initialPooledConnections; i++)
                {
                    this.connectionPool.Add(this.connectionFactory.OpenNewConnection(this.databasePath));
                }

                this.Initialize();
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{nameof(GVFSDatabase)} constructor threw exception setting up connection pool and initializing", ex);
            }
        }

        public static string NormalizePath(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim().Trim(Path.DirectorySeparatorChar);
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.connectionPool.CompleteAdding();
            while (this.connectionPool.TryTake(out IDbConnection connection))
            {
                connection.Dispose();
            }

            this.connectionPool.Dispose();
            this.connectionPool = null;
        }

        IDbConnection IGVFSConnectionPool.GetConnection()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(GVFSDatabase));
            }

            IDbConnection connection;
            if (!this.connectionPool.TryTake(out connection, millisecondsTimeout: MillisecondsWaitingToGetConnection))
            {
                connection = this.connectionFactory.OpenNewConnection(this.databasePath);
            }

            return new GVFSConnection(this, connection);
        }

        private void ReturnToPool(IDbConnection connection)
        {
            if (this.disposed)
            {
                connection.Dispose();
                return;
            }

            try
            {
                this.connectionPool.TryAdd(connection);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                connection.Dispose();
            }
        }

        private void Initialize()
        {
            IGVFSConnectionPool connectionPool = this;
            using (IDbConnection connection = connectionPool.GetConnection())
            using (IDbCommand command = connection.CreateCommand())
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

                PlaceholderTable.CreateTable(connection, GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem);
                SparseTable.CreateTable(connection, GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem);
            }
        }

        /// <summary>
        /// This class is used to wrap a IDbConnection and return it to the connection pool when disposed
        /// </summary>
        private class GVFSConnection : IDbConnection
        {
            private IDbConnection connection;
            private GVFSDatabase database;

            public GVFSConnection(GVFSDatabase database, IDbConnection connection)
            {
                this.database = database;
                this.connection = connection;
            }

            public string ConnectionString
            {
                get => this.connection.ConnectionString;
                set => this.connection.ConnectionString = value;
            }

            public int ConnectionTimeout => this.connection.ConnectionTimeout;

            public string Database => this.connection.Database;

            public ConnectionState State => this.connection.State;

            public IDbTransaction BeginTransaction()
            {
                return this.connection.BeginTransaction();
            }

            public IDbTransaction BeginTransaction(IsolationLevel il)
            {
                return this.connection.BeginTransaction(il);
            }

            public void ChangeDatabase(string databaseName)
            {
                this.connection.ChangeDatabase(databaseName);
            }

            public void Close()
            {
                this.connection.Close();
            }

            public IDbCommand CreateCommand()
            {
                return this.connection.CreateCommand();
            }

            public void Dispose()
            {
                this.database.ReturnToPool(this.connection);
            }

            public void Open()
            {
                this.connection.Open();
            }
        }
    }
}
