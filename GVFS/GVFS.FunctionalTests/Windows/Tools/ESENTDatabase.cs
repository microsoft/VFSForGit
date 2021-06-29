using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Isam.Esent.Collections.Generic;

namespace GVFS.FunctionalTests.Windows.Tools
{
    public static class ESENTDatabase
    {
        public const string EsentRepoMetadataFolder = "RepoMetadata";
        public const string EsentBackgroundOpsFolder = "BackgroundGitUpdates";
        public const string EsentBlobSizesFolder = "BlobSizes";
        public const string EsentPlaceholderFolder = "PlaceholderList";

        private const string DiskLayoutMajorVersionKey = "DiskLayoutVersion";

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

        private static string GetTestDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "Windows", "TestData", fileName);
        }
    }
}
