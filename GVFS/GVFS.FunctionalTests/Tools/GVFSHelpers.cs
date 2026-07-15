using GVFS.Common;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSHelpers
    {
        public const string ModifiedPathsNewLine = "\r\n";
        public const string PlaceholderFieldDelimiter = "\0";

        public static readonly string BackgroundOpsFile = Path.Combine("databases", "BackgroundGitOperations.dat");
        public static readonly string PlaceholderListFile = Path.Combine("databases", "PlaceholderList.dat");
        public static readonly string RepoMetadataName = Path.Combine("databases", "RepoMetadata.dat");

        private const string ModifedPathsLineAddPrefix = "A ";
        private const string ModifedPathsLineDeletePrefix = "D ";

        private const string DiskLayoutMajorVersionKey = "DiskLayoutVersion";
        private const string DiskLayoutMinorVersionKey = "DiskLayoutMinorVersion";
        private const string LocalCacheRootKey = "LocalCacheRoot";
        private const string GitObjectsRootKey = "GitObjectsRoot";
        private const string PlaceholdersNeedUpdate = "PlaceholdersNeedUpdate";
        private const string BlobSizesRootKey = "BlobSizesRoot";

        private const string PrjFSLibPath = "libPrjFSLib.dylib";
        private const int PrjFSResultSuccess = 1;

        private const int WindowsCurrentDiskLayoutMajorVersion = 19;
        private const int MacCurrentDiskLayoutMajorVersion = 19;
        private const int WindowsCurrentDiskLayoutMinimumMajorVersion = 14;
        private const int MacCurrentDiskLayoutMinimumMajorVersion = 18;

        public static string ConvertPathToGitFormat(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator);
        }

        public static void SaveDiskLayoutVersion(string dotGVFSRoot, string majorVersion, string minorVersion)
        {
            SavePersistedValue(dotGVFSRoot, DiskLayoutMajorVersionKey, majorVersion);
            SavePersistedValue(dotGVFSRoot, DiskLayoutMinorVersionKey, minorVersion);
        }

        public static void GetPersistedDiskLayoutVersion(string dotGVFSRoot, out string majorVersion, out string minorVersion)
        {
            majorVersion = GetPersistedValue(dotGVFSRoot, DiskLayoutMajorVersionKey);
            minorVersion = GetPersistedValue(dotGVFSRoot, DiskLayoutMinorVersionKey);
        }

        public static void SaveLocalCacheRoot(string dotGVFSRoot, string value)
        {
            SavePersistedValue(dotGVFSRoot, LocalCacheRootKey, value);
        }

        public static string GetPersistedLocalCacheRoot(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, LocalCacheRootKey);
        }

        public static void SaveGitObjectsRoot(string dotGVFSRoot, string value)
        {
            SavePersistedValue(dotGVFSRoot, GitObjectsRootKey, value);
        }

        public static void SetPlaceholderUpdatesRequired(string dotGVFSRoot, bool isUpdateRequired)
        {
            SavePersistedValue(dotGVFSRoot, PlaceholdersNeedUpdate, isUpdateRequired.ToString());
        }

        public static string GetPersistedGitObjectsRoot(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, GitObjectsRootKey);
        }

        public static string GetPersistedBlobSizesRoot(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, BlobSizesRootKey);
        }

        public static void SQLiteBlobSizesDatabaseHasEntry(string blobSizesDbPath, string blobSha, long blobSize)
        {
            RunSqliteCommand(blobSizesDbPath, command =>
            {
                SqliteParameter shaParam = command.CreateParameter();
                shaParam.ParameterName = "@sha";
                command.CommandText = "SELECT size FROM BlobSizes WHERE sha = (@sha)";
                command.Parameters.Add(shaParam);
                shaParam.Value = StringToShaBytes(blobSha);

                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    reader.Read().ShouldBeTrue();
                    reader.GetInt64(0).ShouldEqual(blobSize);
                }

                return true;
            });
        }

        public static string GetAllSQLitePlaceholdersAsString(string placeholdersDbPath)
        {
            return RunSqliteCommand(placeholdersDbPath, command =>
                {
                    command.CommandText = "SELECT path, pathType, sha FROM Placeholder";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        StringBuilder sb = new StringBuilder();
                        while (reader.Read())
                        {
                            sb.Append(reader.GetString(0));
                            sb.Append(PlaceholderFieldDelimiter);
                            sb.Append(reader.GetByte(1));
                            sb.Append(PlaceholderFieldDelimiter);
                            if (!reader.IsDBNull(2))
                            {
                                sb.Append(reader.GetString(2));
                                sb.Append(PlaceholderFieldDelimiter);
                            }

                            sb.AppendLine();
                        }

                        return sb.ToString();
                    }
                });
        }

        public static void AddPlaceholderFolder(string placeholdersDbPath, string path, int pathType)
        {
            RunSqliteCommand(placeholdersDbPath, command =>
            {
                command.CommandText = "INSERT OR REPLACE INTO Placeholder (path, pathType, sha) VALUES (@path, @pathType, NULL)";
                command.Parameters.AddWithValue("@path", path);
                command.Parameters.AddWithValue("@pathType", pathType);
                return command.ExecuteNonQuery();
            });
        }

        public static void DeletePlaceholder(string placeholdersDbPath, string path)
        {
            RunSqliteCommand(placeholdersDbPath, command =>
            {
                command.CommandText = "DELETE FROM Placeholder WHERE path = @path";
                command.Parameters.AddWithValue("@path", path);
                return command.ExecuteNonQuery();
            });
        }

        public static string ReadAllTextFromWriteLockedFile(string filename)
        {
            // File.ReadAllText and others attempt to open for read and FileShare.None, which always fail on
            // the placeholder db and other files that open for write and only share read access
            using (StreamReader reader = new StreamReader(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Asserts that the modified-paths set, after replaying the on-disk
        /// A/D log, contains exactly <paramref name="gitPaths"/> -- no more,
        /// no fewer. Use this when a test wants to prove that some sequence
        /// of operations produced no spurious modified-paths entries.
        /// </summary>
        public static void ModifiedPathsShouldOnlyContain(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem, params string[] gitPaths)
        {
            HashSet<string> currentPaths = GetCurrentModifiedPaths(enlistment, fileSystem);
            HashSet<string> expectedPaths = new HashSet<string>(gitPaths, FileSystemHelpers.PathComparer);
            currentPaths.SetEquals(expectedPaths).ShouldBeTrue(
                $"Expected modified paths {{{string.Join(",", expectedPaths)}}} but got {{{string.Join(",", currentPaths)}}}");
        }

        public static void ModifiedPathsShouldContain(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem, params string[] gitPaths)
        {
            HashSet<string> currentPaths = GetCurrentModifiedPaths(enlistment, fileSystem);
            foreach (string gitPath in gitPaths)
            {
                currentPaths.ShouldContain(path => path.Equals(gitPath, FileSystemHelpers.PathComparison));
            }
        }

        public static void ModifiedPathsShouldNotContain(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem, params string[] gitPaths)
        {
            HashSet<string> currentPaths = GetCurrentModifiedPaths(enlistment, fileSystem);
            foreach (string gitPath in gitPaths)
            {
                currentPaths.ShouldNotContain(path => path.Equals(gitPath, FileSystemHelpers.PathComparison));
            }
        }

        public static string GetInternalParameter(
            string maintenanceJob = "null",
            string packfileMaintenanceBatchSize = "null",
            string serviceName = null)
        {
            string effectiveServiceName = string.IsNullOrWhiteSpace(serviceName) ? GVFSServiceProcess.TestServiceName : serviceName;
            return $"\"{{\\\"ServiceName\\\":\\\"{effectiveServiceName}\\\"," +
                    "\\\"StartedByService\\\":false," +
                    $"\\\"MaintenanceJob\\\":{maintenanceJob}," +
                    $"\\\"PackfileMaintenanceBatchSize\\\":{packfileMaintenanceBatchSize}}}\"";
        }

        public static void RegisterForOfflineIO()
        {
        }

        public static void UnregisterForOfflineIO()
        {
        }

        public static int GetCurrentDiskLayoutMajorVersion()
        {
            return WindowsCurrentDiskLayoutMajorVersion;
        }

        public static int GetCurrentDiskLayoutMinimumMajorVersion()
        {
            return WindowsCurrentDiskLayoutMinimumMajorVersion;
        }

        private static string GetModifiedPathsContents(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem)
        {
            enlistment.WaitForBackgroundOperations();
            string modifiedPathsDatabase = Path.Combine(enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            return GVFSHelpers.ReadAllTextFromWriteLockedFile(modifiedPathsDatabase);
        }

        /// <summary>
        /// Returns the set of currently-modified paths by replaying the on-disk
        /// modified paths log. The file is append-only between background-op
        /// batches; <see cref="ModifiedPathsDatabase.WriteAllEntriesAndFlush"/>
        /// compacts it after each batch finishes. Because
        /// <see cref="GVFSFunctionalTestEnlistment.WaitForBackgroundOperations"/>
        /// can return after the last task is dequeued but before that compaction
        /// completes, callers must replay the A/D log entries the same way
        /// <see cref="FileBasedCollection.TryLoadFromDisk"/> does on mount to
        /// observe a consistent state.
        /// </summary>
        private static HashSet<string> GetCurrentModifiedPaths(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem)
        {
            string contents = GetModifiedPathsContents(enlistment, fileSystem);
            HashSet<string> paths = new HashSet<string>(FileSystemHelpers.PathComparer);
            foreach (string line in contents.Split(new[] { ModifiedPathsNewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(ModifedPathsLineAddPrefix, StringComparison.Ordinal))
                {
                    paths.Add(line.Substring(ModifedPathsLineAddPrefix.Length));
                }
                else if (line.StartsWith(ModifedPathsLineDeletePrefix, StringComparison.Ordinal))
                {
                    paths.Remove(line.Substring(ModifedPathsLineDeletePrefix.Length));
                }
            }

            return paths;
        }

        private static T RunSqliteCommand<T>(string sqliteDbPath, Func<SqliteCommand, T> runCommand)
        {
            string connectionString = $"data source={sqliteDbPath}";
            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    return runCommand(command);
                }
            }
        }

        private static byte[] StringToShaBytes(string sha)
        {
            byte[] shaBytes = new byte[20];

            string upperCaseSha = sha.ToUpper();
            int stringIndex = 0;
            for (int i = 0; i < 20; ++i)
            {
                stringIndex = i * 2;
                char firstChar = sha[stringIndex];
                char secondChar = sha[stringIndex + 1];
                shaBytes[i] = (byte)(CharToByte(firstChar) << 4 | CharToByte(secondChar));
            }

            return shaBytes;
        }

        private static byte CharToByte(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return (byte)(c - '0');
            }

            if (c >= 'A' && c <= 'F')
            {
                return (byte)(10 + (c - 'A'));
            }

            Assert.Fail($"Invalid character c: {c}");

            return 0;
        }

        private static string GetPersistedValue(string dotGVFSRoot, string key)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, RepoMetadataName);
            string json;
            using (FileStream fs = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(fs))
            {
                while (!reader.EndOfStream)
                {
                    json = reader.ReadLine();
                    json.Substring(0, 2).ShouldEqual("A ");

                    KeyValuePair<string, string> kvp = GVFSJsonOptions.Deserialize<KeyValuePair<string, string>>(json.Substring(2));
                    if (kvp.Key == key)
                    {
                        return kvp.Value;
                    }
                }
            }

            return null;
        }

        private static void SavePersistedValue(string dotGVFSRoot, string key, string value)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, RepoMetadataName);

            Dictionary<string, string> repoMetadata = new Dictionary<string, string>();
            string json;
            using (FileStream fs = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(fs))
            {
                while (!reader.EndOfStream)
                {
                    json = reader.ReadLine();
                    json.Substring(0, 2).ShouldEqual("A ");

                    KeyValuePair<string, string> kvp = GVFSJsonOptions.Deserialize<KeyValuePair<string, string>>(json.Substring(2));
                    repoMetadata.Add(kvp.Key, kvp.Value);
                }
            }

            repoMetadata[key] = value;

            string newRepoMetadataContents = string.Empty;

            foreach (KeyValuePair<string, string> kvp in repoMetadata)
            {
                newRepoMetadataContents += "A " + GVFSJsonOptions.Serialize(kvp).Trim() + "\r\n";
            }

            File.WriteAllText(metadataPath, newRepoMetadataContents);
        }

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_RegisterForOfflineIO")]
        private static extern uint PrjFSRegisterForOfflineIO();

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_UnregisterForOfflineIO")]
        private static extern uint PrjFSUnregisterForOfflineIO();
    }
}
