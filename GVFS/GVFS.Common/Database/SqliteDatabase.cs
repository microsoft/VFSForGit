using GVFS.Common.FileSystem;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace GVFS.Common.Database
{
    /// <summary>
    /// Handles creating connections to SQLite database and checking for issues with the database
    /// </summary>
    public class SqliteDatabase : IDbConnectionFactory
    {
        public static bool HasIssue(string databasePath, PhysicalFileSystem filesystem, out string issue)
        {
            issue = null;

            if (filesystem.FileExists(databasePath))
            {
                List<string> integrityCheckResults = new List<string>();

                try
                {
                    string sqliteConnectionString = CreateConnectionString(databasePath);
                    using (SqliteConnection integrityConnection = new SqliteConnection(sqliteConnectionString))
                    {
                        integrityConnection.Open();

                        using (SqliteCommand pragmaCommand = integrityConnection.CreateCommand())
                        {
                            pragmaCommand.CommandText = "PRAGMA integrity_check;";
                            using (SqliteDataReader reader = pragmaCommand.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    integrityCheckResults.Add(reader.GetString(0));
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    issue = $"Exception while trying to access {databasePath}: {e.Message}";
                    return true;
                }

                // If pragma integrity_check finds no errors, a single row with the value 'ok' is returned
                // http://www.sqlite.org/pragma.html#pragma_integrity_check
                if (integrityCheckResults.Count != 1 || integrityCheckResults[0] != "ok")
                {
                    issue = string.Join(",", integrityCheckResults);
                    return true;
                }
            }

            return false;
        }

        public static string CreateConnectionString(string databasePath)
        {
            // Share-Cache mode allows multiple connections from the same process to share the same data cache
            // http://www.sqlite.org/sharedcache.html
            return $"data source={databasePath};Cache=Shared";
        }

        public IDbConnection OpenNewConnection(string databasePath)
        {
            SqliteConnection connection = new SqliteConnection(CreateConnectionString(databasePath));
            connection.Open();
            return connection;
        }
    }
}
