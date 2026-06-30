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
        /// Returns true if the error code represents a transient condition
        /// that may resolve on retry (I/O errors, locking contention).
        /// </summary>
        public static bool IsTransientError(int sqliteErrorCode)
        {
            return sqliteErrorCode == DiskIOError
                || sqliteErrorCode == Busy
                || sqliteErrorCode == Locked;
        }
    }
}
