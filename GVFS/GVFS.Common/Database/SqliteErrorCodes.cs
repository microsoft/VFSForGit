namespace GVFS.Common.Database
{
    /// <summary>
    /// SQLite result codes used for error classification.
    /// See https://www.sqlite.org/rescode.html
    /// </summary>
    public static class SqliteErrorCodes
    {
        /// <summary>SQLITE_CORRUPT (11) — database disk image is malformed</summary>
        public const int Corrupt = 11;

        /// <summary>SQLITE_NOTADB (26) — file is not a database</summary>
        public const int NotADatabase = 26;
    }
}
