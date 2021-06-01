using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GVFS.Virtualization.BlobSize
{
    public class BlobSizes : IDisposable
    {
        public const string DatabaseName = "BlobSizes.sql";

        private const string EtwArea = nameof(BlobSizes);
        private const int SaveSizesRetryDelayMS = 50;

        private readonly string databasePath;
        private readonly string sqliteConnectionString;

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;

        private Thread flushDataThread;
        private AutoResetEvent wakeUpFlushThread;
        private bool isStopping;
        private ConcurrentQueue<BlobSize> queuedSizes;

        public BlobSizes(string blobSizesRoot, PhysicalFileSystem fileSystem, ITracer tracer)
        {
            this.databasePath = Path.Combine(blobSizesRoot, DatabaseName);
            this.fileSystem = fileSystem;
            this.tracer = tracer;
            this.wakeUpFlushThread = new AutoResetEvent(false);
            this.queuedSizes = new ConcurrentQueue<BlobSize>();
            this.sqliteConnectionString = SqliteDatabase.CreateConnectionString(this.databasePath);
        }

        /// <summary>
        /// Create a connection to BlobSizes that can be used for saving and retrieving blob sizes
        /// </summary>
        /// <returns>BlobSizesConnection</returns>
        /// <remarks>BlobSizesConnection are thread-specific</remarks>
        public virtual BlobSizesConnection CreateConnection()
        {
            return new BlobSizesConnection(this, this.sqliteConnectionString);
        }

        public virtual void Initialize()
        {
            string folderPath = Path.GetDirectoryName(this.databasePath);
            this.fileSystem.CreateDirectory(folderPath);

            using (SqliteConnection connection = new SqliteConnection(this.sqliteConnectionString))
            {
                connection.Open();

                using (SqliteCommand pragmaWalCommand = connection.CreateCommand())
                {
                    // Advantages of using WAL ("Write-Ahead Log")
                    // 1. WAL is significantly faster in most scenarios.
                    // 2. WAL provides more concurrency as readers do not block writers and a writer does not block readers.
                    //    Reading and writing can proceed concurrently.
                    // 3. Disk I/O operations tends to be more sequential using WAL.
                    // 4. WAL uses many fewer fsync() operations and is thus less vulnerable to problems on systems
                    //    where the fsync() system call is broken.
                    // http://www.sqlite.org/wal.html
                    pragmaWalCommand.CommandText = $"PRAGMA journal_mode=WAL;";
                    pragmaWalCommand.ExecuteNonQuery();
                }

                using (SqliteCommand pragmaCacheSizeCommand = connection.CreateCommand())
                {
                    // If the argument N is negative, then the number of cache pages is adjusted to use approximately abs(N*1024) bytes of memory
                    // -40000 => 40,000 * 1024 bytes => ~39MB
                    pragmaCacheSizeCommand.CommandText = $"PRAGMA cache_size=-40000;";
                    pragmaCacheSizeCommand.ExecuteNonQuery();
                }

                EventMetadata databaseMetadata = this.CreateEventMetadata();

                using (SqliteCommand userVersionCommand = connection.CreateCommand())
                {
                    // The user_version pragma will to get or set the value of the user-version integer at offset 60 in the database header.
                    // The user-version is an integer that is available to applications to use however they want. SQLite makes no use of the user-version itself.
                    // https://sqlite.org/pragma.html#pragma_user_version
                    userVersionCommand.CommandText = $"PRAGMA user_version;";

                    object userVersion = userVersionCommand.ExecuteScalar();

                    if (userVersion == null || Convert.ToInt64(userVersion) < 1)
                    {
                        userVersionCommand.CommandText = $"PRAGMA user_version=1;";
                        userVersionCommand.ExecuteNonQuery();
                        this.tracer.RelatedInfo($"{nameof(BlobSize)}.{nameof(this.Initialize)}: setting user_version to 1");
                    }
                    else
                    {
                        databaseMetadata.Add("user_version", Convert.ToInt64(userVersion));
                    }
                }

                using (SqliteCommand pragmaSynchronousCommand = connection.CreateCommand())
                {
                    // GVFS uses the default value (FULL) to reduce the risks of corruption
                    // http://www.sqlite.org/pragma.html#pragma_synchronous
                    // (Note: This call is to retrieve the value of 'synchronous' and log it)
                    pragmaSynchronousCommand.CommandText = $"PRAGMA synchronous;";
                    object synchronous = pragmaSynchronousCommand.ExecuteScalar();
                    if (synchronous != null)
                    {
                        databaseMetadata.Add("synchronous", Convert.ToInt64(synchronous));
                    }
                }

                this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(BlobSize)}_{nameof(this.Initialize)}_db_settings", databaseMetadata);

                using (SqliteCommand createTableCommand = connection.CreateCommand())
                {
                    // Use a BLOB for sha rather than a string to reduce the size of the database
                    createTableCommand.CommandText = @"CREATE TABLE IF NOT EXISTS [BlobSizes] (sha BLOB, size INT, PRIMARY KEY (sha));";
                    createTableCommand.ExecuteNonQuery();
                }
            }

            this.flushDataThread = new Thread(this.FlushDbThreadMain);
            this.flushDataThread.IsBackground = true;
            this.flushDataThread.Start();
        }

        public virtual void Shutdown()
        {
            this.isStopping = true;
            this.wakeUpFlushThread.Set();
            this.flushDataThread.Join();
        }

        public virtual void AddSize(Sha1Id sha, long size)
        {
            this.queuedSizes.Enqueue(new BlobSize(sha, size));
        }

        public virtual void Flush()
        {
            this.wakeUpFlushThread.Set();
        }

        public void Dispose()
        {
            if (this.wakeUpFlushThread != null)
            {
                this.wakeUpFlushThread.Dispose();
                this.wakeUpFlushThread = null;
            }
        }

        private void FlushDbThreadMain()
        {
            try
            {
                int errorCode;
                string error;
                ulong failCount;

                using (BlobSizesDatabaseWriter sizeWriter = new BlobSizesDatabaseWriter(this.sqliteConnectionString))
                {
                    sizeWriter.Initialize();

                    while (true)
                    {
                        this.wakeUpFlushThread.WaitOne();

                        failCount = 0;

                        while (!sizeWriter.TryAddSizes(this.queuedSizes, out errorCode, out error) && !this.isStopping)
                        {
                            ++failCount;
                            if (failCount % 200UL == 1)
                            {
                                EventMetadata metadata = this.CreateEventMetadata();
                                metadata.Add(nameof(errorCode), errorCode);
                                metadata.Add(nameof(error), error);
                                metadata.Add(nameof(failCount), failCount);
                                this.tracer.RelatedWarning(metadata, $"{nameof(this.flushDataThread)}: {nameof(BlobSizesDatabaseWriter.TryAddSizes)} failed");
                            }

                            Thread.Sleep(SaveSizesRetryDelayMS);
                        }

                        if (this.isStopping)
                        {
                            return;
                        }
                        else if (failCount > 1)
                        {
                            EventMetadata metadata = this.CreateEventMetadata();
                            metadata.Add(nameof(failCount), failCount);
                            this.tracer.RelatedEvent(
                                EventLevel.Informational,
                                $"{nameof(this.FlushDbThreadMain)}_{nameof(BlobSizesDatabaseWriter.TryAddSizes)}_SucceededAfterFailing",
                                metadata);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                this.LogErrorAndExit("FlushDbThreadMain caught unhandled exception, exiting process", e);
            }
        }

        private void LogErrorAndExit(string message, Exception e = null)
        {
            EventMetadata metadata = this.CreateEventMetadata(e);
            this.tracer.RelatedError(metadata, message);
            Environment.Exit(1);
        }

        private EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        public class BlobSizesConnection : IDisposable
        {
            private string connectionString;

            // Keep connection and command alive for the duration of BlobSizesConnection so that
            // the prepared SQLite statement can be reused
            private SqliteConnection connection;
            private SqliteCommand querySizeCommand;
            private SqliteParameter shaParam;

            private byte[] shaBuffer;

            public BlobSizesConnection(BlobSizes blobSizes)
            {
                // For unit testing
                this.BlobSizesDatabase = blobSizes;
            }

            public BlobSizesConnection(BlobSizes blobSizes, string connectionString)
            {
                this.BlobSizesDatabase = blobSizes;
                this.connectionString = connectionString;
                this.shaBuffer = new byte[20];

                try
                {
                    this.connection = new SqliteConnection(this.connectionString);
                    this.connection.Open();

                    using (SqliteCommand pragmaReadUncommittedCommand = this.connection.CreateCommand())
                    {
                        // A database connection in read-uncommitted mode does not attempt to obtain read-locks
                        // before reading from database tables as described above. This can lead to inconsistent
                        // query results if another database connection modifies a table while it is being read,
                        // but it also means that a read-transaction opened by a connection in read-uncommitted
                        // mode can neither block nor be blocked by any other connection
                        // http://www.sqlite.org/pragma.html#pragma_read_uncommitted
                        pragmaReadUncommittedCommand.CommandText = $"PRAGMA read_uncommitted=1;";
                        pragmaReadUncommittedCommand.ExecuteNonQuery();
                    }

                    this.querySizeCommand = this.connection.CreateCommand();

                    this.shaParam = this.querySizeCommand.CreateParameter();
                    this.shaParam.ParameterName = "@sha";

                    this.querySizeCommand.CommandText = "SELECT size FROM BlobSizes WHERE sha = (@sha);";
                    this.querySizeCommand.Parameters.Add(this.shaParam);
                    this.querySizeCommand.Prepare();
                }
                catch (Exception e)
                {
                    if (this.querySizeCommand != null)
                    {
                        this.querySizeCommand.Dispose();
                        this.querySizeCommand = null;
                    }

                    if (this.connection != null)
                    {
                        this.connection.Dispose();
                        this.connection = null;
                    }

                    throw new BlobSizesException(e);
                }
            }

            public BlobSizes BlobSizesDatabase { get; }

            public virtual bool TryGetSize(Sha1Id sha, out long length)
            {
                try
                {
                    length = -1;

                    sha.ToBuffer(this.shaBuffer);
                    this.shaParam.Value = this.shaBuffer;

                    using (SqliteDataReader reader = this.querySizeCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            length = reader.GetInt64(0);
                            return true;
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new BlobSizesException(e);
                }

                return false;
            }

            public void Dispose()
            {
                if (this.querySizeCommand != null)
                {
                    this.querySizeCommand.Dispose();
                    this.querySizeCommand = null;
                }

                if (this.connection != null)
                {
                    this.connection.Dispose();
                    this.connection = null;
                }
            }
        }

        private class BlobSize
        {
            public BlobSize(Sha1Id sha, long size)
            {
                this.Sha = sha;
                this.Size = size;
            }

            public Sha1Id Sha { get; }
            public long Size { get; }
        }

        private class BlobSizesDatabaseWriter : IDisposable
        {
            private string connectionString;

            private SqliteConnection connection;
            private SqliteCommand addCommand;
            private SqliteParameter shaParam;
            private SqliteParameter sizeParam;

            private byte[] shaBuffer;

            public BlobSizesDatabaseWriter(string connectionString)
            {
                this.connectionString = connectionString;
                this.shaBuffer = new byte[20];
            }

            public void Initialize()
            {
                this.connection = new SqliteConnection(this.connectionString);
                this.connection.Open();

                this.addCommand = this.connection.CreateCommand();

                this.shaParam = this.addCommand.CreateParameter();
                this.shaParam.ParameterName = "@sha";

                this.sizeParam = this.addCommand.CreateParameter();
                this.sizeParam.ParameterName = "@size";

                this.addCommand.CommandText = $"INSERT OR IGNORE INTO BlobSizes (sha, size) VALUES (@sha, @size);";
                this.addCommand.Parameters.Add(this.shaParam);
                this.addCommand.Parameters.Add(this.sizeParam);

                this.addCommand.Prepare();
            }

            public bool TryAddSizes(ConcurrentQueue<BlobSize> sizes, out int errorCode, out string error)
            {
                errorCode = 0;
                error = null;

                try
                {
                    using (SqliteTransaction insertTransaction = this.connection.BeginTransaction())
                    {
                        this.addCommand.Transaction = insertTransaction;

                        BlobSize blobSize;
                        while (sizes.TryDequeue(out blobSize))
                        {
                            blobSize.Sha.ToBuffer(this.shaBuffer);
                            this.shaParam.Value = this.shaBuffer;
                            this.sizeParam.Value = blobSize.Size;
                            this.addCommand.ExecuteNonQuery();
                        }

                        insertTransaction.Commit();
                    }
                }
                catch (SqliteException e)
                {
                    errorCode = e.SqliteErrorCode;
                    error = e.Message;
                    return false;
                }

                return true;
            }

            public void Dispose()
            {
                if (this.addCommand != null)
                {
                    this.addCommand.Dispose();
                    this.connection = null;
                }

                if (this.connection != null)
                {
                    this.connection.Dispose();
                    this.connection = null;
                }
            }
        }
    }
}
