using GVFS.Tests.Should;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json;
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

        private const string DiskLayoutVersionKey = "DiskLayoutVersion";
        private const string LocalCacheRootKey = "LocalCacheRoot";
        private const string GitObjectsRootKey = "GitObjectsRoot";

        public static void SaveDiskLayoutVersion(string dotGVFSRoot, string value)
        {
            SavePersistedValue(dotGVFSRoot, DiskLayoutVersionKey, value);
        }

        public static string GetPersistedDiskLayoutVersion(string dotGVFSRoot)
        {
            return GetPersistedValue(dotGVFSRoot, DiskLayoutVersionKey);
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

        public static void SaveDiskLayoutVersionAsEsentDatabase(string dotGVFSRoot, string value)
        {
            string metadataPath = Path.Combine(dotGVFSRoot, EsentRepoMetadataFolder);
            using (PersistentDictionary<string, string> repoMetadata = new PersistentDictionary<string, string>(metadataPath))
            {
                repoMetadata[DiskLayoutVersionKey] = value;
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
