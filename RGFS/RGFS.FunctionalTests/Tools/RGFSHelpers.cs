using RGFS.Tests.Should;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RGFS.FunctionalTests.Tools
{
    public static class RGFSHelpers
    {
        public const string EsentRepoMetadataFolder = "RepoMetadata";
        public const string EsentBackgroundOpsFolder = "BackgroundGitUpdates";
        public const string EsentBlobSizesFolder = "BlobSizes";
        public const string EsentPlaceholderFolder = "PlaceholderList";

        public const string BackgroundOpsFile = "databases\\BackgroundGitOperations.dat";
        public const string PlaceholderListFile = "databases\\PlaceholderList.dat";
        public const string RepoMetadataName = "databases\\RepoMetadata.dat";

        private const string DiskLayoutVersionKey = "DiskLayoutVersion";

        public static void SaveDiskLayoutVersion(string dotRGFSRoot, string value)
        {
            string metadataPath = Path.Combine(dotRGFSRoot, RepoMetadataName);
            File.WriteAllText(metadataPath, "A {\"Key\":\"" + DiskLayoutVersionKey + "\", \"Value\":\"" + value + "\"}\r\n");
        }

        public static string GetPersistedDiskLayoutVersion(string dotRGFSRoot)
        {
            string metadataPath = Path.Combine(dotRGFSRoot, RepoMetadataName);
            string json;
            using (FileStream fs = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                json = new StreamReader(fs).ReadToEnd();
            }

            json.Substring(0, 2).ShouldEqual("A ");
            json.Substring(json.Length - 2).ShouldEqual("\r\n");

            JObject metadata = JObject.Parse(json.Substring(2, json.Length - 4));
            KeyValuePair<string, string> kvp = metadata.ToObject<KeyValuePair<string, string>>();
            return kvp.Value;
        }

        public static void SaveDiskLayoutVersionAsEsentDatabase(string dotRGFSRoot, string value)
        {
            string metadataPath = Path.Combine(dotRGFSRoot, EsentRepoMetadataFolder);
            using (PersistentDictionary<string, string> repoMetadata = new PersistentDictionary<string, string>(metadataPath))
            {
                repoMetadata[DiskLayoutVersionKey] = value;
                repoMetadata.Flush();
            }
        }

        public static void CreateEsentPlaceholderDatabase(string dotRGFSRoot)
        {
            string metadataPath = Path.Combine(dotRGFSRoot, EsentPlaceholderFolder);
            using (PersistentDictionary<string, string> placeholders = new PersistentDictionary<string, string>(metadataPath))
            {
                placeholders["mock:\\path"] = new string('0', 40);
                placeholders.Flush();
            }
        }

        public static void CreateEsentBackgroundOpsDatabase(string dotRGFSRoot)
        {
            // Copies an ESENT DB with a single entry:
            // Operation=6 (OnFirstWrite) Path=.gitattributes VirtualPath=.gitattributes Id=1
            string testDataPath = GetTestDataPath(EsentBackgroundOpsFolder);
            string metadataPath = Path.Combine(dotRGFSRoot, EsentBackgroundOpsFolder);
            Directory.CreateDirectory(metadataPath);
            foreach (string filepath in Directory.EnumerateFiles(testDataPath))
            {
                string filename = Path.GetFileName(filepath);
                File.Copy(filepath, Path.Combine(metadataPath, filename));
            }
        }
        
        private static string GetTestDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "TestData", fileName);
        }
    }
}
