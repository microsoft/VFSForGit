namespace GVFS.Common.Database
{
    /// <summary>
    /// SQLite result codes used for error classification.
    /// See https://www.sqlite.org/rescode.html
    /// </summary>
    public static class SqliteErrorCodes
    {
        /// <summary>SQLITE_BUSY (5) — database file is locked by another connection</summary>
        public const int Busy = 5;

        /// <summary>SQLITE_LOCKED (6) — a table in the database is locked</summary>
        public const int Locked = 6;

        /// <summary>SQLITE_IOERR (10) — disk I/O error</summary>
        public const int DiskIOError = 10;

        /// <summary>SQLITE_CORRUPT (11) — database disk image is malformed</summary>
        public const int Corrupt = 11;

        /// <summary>SQLITE_NOTADB (26) — file is not a database</summary>
        public const int NotADatabase = 26;

        /// <summary>
        /// Returns true if the error code represents a transient locking condition
        /// that is safe to retry. Only BUSY and LOCKED qualify: both mean the operation
        /// was blocked before executing and was never committed, so retrying is idempotent.
        ///
        /// SQLITE_IOERR is intentionally excluded. An I/O error can fire during connection
        /// disposal AFTER a write has already committed (e.g. WAL flush on close). Retrying
        /// in that case would re-execute the read phase on an already-empty table and return
        /// stale results — a risk that exists in ExecuteReadThenWrite (RemoveAllEntriesForFolder).
        /// IOERR on a genuinely failed write will have been rolled back by SQLite atomically,
        /// but we cannot distinguish that case at the catch site without inspecting whether
        /// the write had already committed. The safe choice is to surface IOERR immediately.
        /// </summary>
        public static bool IsTransientError(int sqliteErrorCode)
        {
            return sqliteErrorCode == Busy
                || sqliteErrorCode == Locked;
        }
    }
}
