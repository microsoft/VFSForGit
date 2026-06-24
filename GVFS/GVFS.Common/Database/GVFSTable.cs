using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Base class for GVFS SQLite tables. Provides connection pooling,
    /// writer serialization, and transient error retry logic.
    /// </summary>
    public abstract class GVFSTable
    {
        private const int MaxRetries = 5;
        private const int BaseRetryDelayMs = 50;

        private readonly IGVFSConnectionPool connectionPool;
        private readonly Lock writerLock = new Lock();

        protected GVFSTable(IGVFSConnectionPool connectionPool)
        {
            this.connectionPool = connectionPool;
        }

        /// <summary>
        /// Name of the concrete table class, used in exception messages.
        /// </summary>
        protected abstract string TableName { get; }

        /// <summary>
        /// Executes a read operation with retry on transient SQLite errors.
        /// Throws GVFSDatabaseException on non-transient or exhausted retries.
        /// </summary>
        protected T ExecuteRead<T>(Func<IDbCommand, T> operation, [CallerMemberName] string caller = null)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    using (IDbConnection connection = this.connectionPool.GetConnection())
                    using (IDbCommand command = connection.CreateCommand())
                    {
                        return operation(command);
                    }
                }
                catch (SqliteException ex) when (SqliteErrorCodes.IsTransientError(ex.SqliteErrorCode) && attempt < MaxRetries)
                {
                    attempt++;
                    Thread.Sleep(BaseRetryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    throw new GVFSDatabaseException($"{this.TableName}.{caller} Exception", ex);
                }
            }
        }

        /// <summary>
        /// Executes a read operation that tolerates transient errors by returning
        /// a fallback value. Used for non-critical paths like heartbeat telemetry.
        /// </summary>
        protected T ExecuteNonCriticalRead<T>(Func<IDbCommand, T> operation, T fallbackValue, [CallerMemberName] string caller = null)
        {
            try
            {
                using (IDbConnection connection = this.connectionPool.GetConnection())
                using (IDbCommand command = connection.CreateCommand())
                {
                    return operation(command);
                }
            }
            catch (SqliteException ex) when (SqliteErrorCodes.IsTransientError(ex.SqliteErrorCode))
            {
                return fallbackValue;
            }
            catch (Exception ex)
            {
                throw new GVFSDatabaseException($"{this.TableName}.{caller} Exception", ex);
            }
        }

        /// <summary>
        /// Executes a write operation (under writer lock) with retry on transient SQLite errors.
        /// Throws GVFSDatabaseException on non-transient or exhausted retries.
        /// </summary>
        protected void ExecuteWrite(Action<IDbCommand> operation, [CallerMemberName] string caller = null)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    using (IDbConnection connection = this.connectionPool.GetConnection())
                    using (IDbCommand command = connection.CreateCommand())
                    {
                        lock (this.writerLock)
                        {
                            operation(command);
                        }
                    }

                    return;
                }
                catch (SqliteException ex) when (SqliteErrorCodes.IsTransientError(ex.SqliteErrorCode) && attempt < MaxRetries)
                {
                    attempt++;
                    Thread.Sleep(BaseRetryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    throw new GVFSDatabaseException($"{this.TableName}.{caller} Exception", ex);
                }
            }
        }

        /// <summary>
        /// Executes a read-then-write operation on the same connection. The entire
        /// operation retries on transient errors. The caller is responsible for
        /// performing any write under the writer lock via <see cref="WriterLock"/>.
        /// </summary>
        protected T ExecuteReadThenWrite<T>(Func<IDbCommand, T> readThenWrite, [CallerMemberName] string caller = null)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    using (IDbConnection connection = this.connectionPool.GetConnection())
                    using (IDbCommand command = connection.CreateCommand())
                    {
                        return readThenWrite(command);
                    }
                }
                catch (SqliteException ex) when (SqliteErrorCodes.IsTransientError(ex.SqliteErrorCode) && attempt < MaxRetries)
                {
                    attempt++;
                    Thread.Sleep(BaseRetryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    throw new GVFSDatabaseException($"{this.TableName}.{caller} Exception", ex);
                }
            }
        }

        /// <summary>
        /// Lock object for serializing write operations. Exposed to subclasses
        /// that need mixed read-then-write within <see cref="ExecuteReadThenWrite{T}"/>.
        /// </summary>
        protected Lock WriterLock => this.writerLock;
    }
}
