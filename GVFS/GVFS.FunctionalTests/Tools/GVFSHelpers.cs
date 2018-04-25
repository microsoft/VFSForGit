using GVFS.Tests.Should;
using Microsoft.Data.Sqlite;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSHelpers
    {
        public const string EsentRepoMetadataFolder = "RepoMetadata";
        public const string EsentBackgroundOpsFolder = "BackgroundGitUpdates";
        public const string EsentBlobSizesFolder = "BlobSizes";
        public const string EsentPlaceholderFolder = "PlaceholderList";

        public const string BackgroundOpsFile = "databases\\BackgroundGitOperations.dat";
        public const string PlaceholderListFile = "databases\\PlaceholderList.dat";
        public const string RepoMetadataName = "databases\\RepoMetadata.dat";

        private const string DiskLayoutMajorVersionKey = "DiskLayoutVersion";
        private const string DiskLayoutMinorVersionKey = "DiskLayoutMinorVersion";
        private const string LocalCacheRootKey = "LocalCacheRoot";
        private const string GitObjectsRootKey = "GitObjectsRoot";
        private const string BlobSizesRootKey = "BlobSizesRoot";

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

        public static string GetPersistedGitObjectsRoot(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, GitObjectsRootKey);
        }

        public static string GetPersistedBlobSizesRoot(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, BlobSizesRootKey);
        }

        public static void SaveDiskLayoutVersionAsEsentDatabase(string dotGVFSRoot, string majorVersion)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, EsentRepoMetadataFolder);
            using (PersistentDictionary<string, string> repoMetadata = new PersistentDictionary<string, string>(metadataPath))
            {
                repoMetadata[DiskLayoutMajorVersionKey] = majorVersion;
                repoMetadata.Flush();
            }
        }

        public static void CreateEsentPlaceholderDatabase(string dotGVFSRoot)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, EsentPlaceholderFolder);
            using (PersistentDictionary<string, string> placeholders = new PersistentDictionary<string, string>(metadataPath))
            {
                placeholders["mock:\\path"] = new string('0', 40);
                placeholders.Flush();
            }
        }

        public static void CreateEsentBackgroundOpsDatabase(string dotGVFSRoot)
        {
            // Copies an ESENT DB with a single entry:
            // Operation=6 (OnFirstWrite) Path=.gitattributes VirtualPath=.gitattributes Id=1
            string testDataPath = GetTestDataPath(EsentBackgroundOpsFolder);
            string metadataPath = Path.Combine(dotGVFSRoot, EsentBackgroundOpsFolder);
            Directory.CreateDirectory(metadataPath);
            foreach (string filepath in Directory.EnumerateFiles(testDataPath))
            {
                string filename = Path.GetFileName(filepath);
                File.Copy(filepath, Path.Combine(metadataPath, filename));
            }
        }

        public static void CreateEsentBlobSizesDatabase(string dotGVFSRoot, List<KeyValuePair<string, long>> entries)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, EsentBlobSizesFolder);
            using (PersistentDictionary<string, long> blobSizes = new PersistentDictionary<string, long>(metadataPath))
            {
                foreach (KeyValuePair<string, long> entry in entries)
                {
                    blobSizes[entry.Key] = entry.Value;
                }

                blobSizes.Flush();
            }
        }

        public static void SQLiteBlobSizesDatabaseHasEntry(string blobSizesDbPath, string blobSha, long blobSize)
        {
            string connectionString = $"data source={blobSizesDbPath}";
            using (SqliteConnection readConnection = new SqliteConnection(connectionString))
            {
                readConnection.Open();
                using (SqliteCommand selectCommand = readConnection.CreateCommand())
                {
                    SqliteParameter shaParam = selectCommand.CreateParameter();
                    shaParam.ParameterName = "@sha";
                    selectCommand.CommandText = "SELECT size FROM BlobSizes WHERE sha = (@sha)";
                    selectCommand.Parameters.Add(shaParam);
                    shaParam.Value = StringToShaBytes(blobSha);

                    using (SqliteDataReader reader = selectCommand.ExecuteReader())
                    {
                        reader.Read().ShouldBeTrue();
                        reader.GetInt64(0).ShouldEqual(blobSize);
                    }
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

                    KeyValuePair<string, string> kvp = JsonConvert.DeserializeObject<KeyValuePair<string, string>>(json.Substring(2));
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

                    KeyValuePair<string, string> kvp = JsonConvert.DeserializeObject<KeyValuePair<string, string>>(json.Substring(2));
                    repoMetadata.Add(kvp.Key, kvp.Value);
                }
            }

            repoMetadata[key] = value;

            string newRepoMetadataContents = string.Empty;

            foreach (KeyValuePair<string, string> kvp in repoMetadata)
            {
                newRepoMetadataContents += "A " + JsonConvert.SerializeObject(kvp).Trim() + "\r\n";
            }

            File.WriteAllText(metadataPath, newRepoMetadataContents);
        }

        private static string GetTestDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "TestData", fileName);
        }
    }
}
