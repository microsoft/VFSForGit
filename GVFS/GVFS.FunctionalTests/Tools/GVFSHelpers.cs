using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSHelpers
    {
        public const string ModifiedPathsNewLine = "\r\n";

        public static readonly string BackgroundOpsFile = Path.Combine("databases", "BackgroundGitOperations.dat");
        public static readonly string PlaceholderListFile = Path.Combine("databases", "PlaceholderList.dat");
        public static readonly string RepoMetadataName = Path.Combine("databases", "RepoMetadata.dat");

        private const string ModifedPathsLineAddPrefix = "A ";
        private const string ModifedPathsLineDeletePrefix = "D ";

        private const string DiskLayoutMajorVersionKey = "DiskLayoutVersion";
        private const string DiskLayoutMinorVersionKey = "DiskLayoutMinorVersion";
        private const string LocalCacheRootKey = "LocalCacheRoot";
        private const string GitObjectsRootKey = "GitObjectsRoot";
        private const string BlobSizesRootKey = "BlobSizesRoot";

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

        public static string ReadAllTextFromWriteLockedFile(string filename)
        {
            // File.ReadAllText and others attempt to open for read and FileShare.None, which always fail on 
            // the placeholder db and other files that open for write and only share read access
            using (StreamReader reader = new StreamReader(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                return reader.ReadToEnd();
            }
        }

        public static void ModifiedPathsContentsShouldEqual(FileSystemRunner fileSystem, string dotGVFSRoot, string contents)
        {
            string modifiedPathsDatabase = Path.Combine(dotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            GVFSHelpers.ReadAllTextFromWriteLockedFile(modifiedPathsDatabase).ShouldEqual(contents);
        }

        public static void ModifiedPathsShouldContain(FileSystemRunner fileSystem, string dotGVFSRoot, params string[] gitPaths)
        {
            string modifiedPathsDatabase = Path.Combine(dotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            string modifedPathsContents = GVFSHelpers.ReadAllTextFromWriteLockedFile(modifiedPathsDatabase);
            string[] modifedPathLines = modifedPathsContents.Split(new[] { ModifiedPathsNewLine }, StringSplitOptions.None);
            foreach (string gitPath in gitPaths)
            {
                modifedPathLines.ShouldContain(path => path.Equals(ModifedPathsLineAddPrefix + gitPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static void ModifiedPathsShouldNotContain(FileSystemRunner fileSystem, string dotGVFSRoot, params string[] gitPaths)
        {
            string modifiedPathsDatabase = Path.Combine(dotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            modifiedPathsDatabase.ShouldBeAFile(fileSystem);
            string modifedPathsContents = GVFSHelpers.ReadAllTextFromWriteLockedFile(modifiedPathsDatabase);
            string[] modifedPathLines = modifedPathsContents.Split(new[] { ModifiedPathsNewLine }, StringSplitOptions.None);
            foreach (string gitPath in gitPaths)
            {
                modifedPathLines.ShouldNotContain(
                    path =>
                    {
                        return path.Equals(ModifedPathsLineAddPrefix + gitPath, StringComparison.OrdinalIgnoreCase) ||
                               path.Equals(ModifedPathsLineDeletePrefix + gitPath, StringComparison.OrdinalIgnoreCase);
                    });
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
    }
}
