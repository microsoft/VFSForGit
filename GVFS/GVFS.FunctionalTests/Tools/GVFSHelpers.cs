using Microsoft.Isam.Esent.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSHelpers
    {
        public const string RepoMetadataDatabaseName = "RepoMetadata";
        private const string DiskLayoutVersionKey = "DiskLayoutVersion";

        public static void SaveDiskLayoutVersion(string dotGVFSRoot, string value)
        {
            using (PersistentDictionary<string, string> dictionary = new PersistentDictionary<string, string>(
                   Path.Combine(dotGVFSRoot, RepoMetadataDatabaseName)))
            {
                dictionary[DiskLayoutVersionKey] = value;
                dictionary.Flush();
            }
        }

        public static string GetPersistedDiskLayoutVersion(string dotGVFSRoot)
        {
            using (PersistentDictionary<string, string> dictionary = new PersistentDictionary<string, string>(
                   Path.Combine(dotGVFSRoot, RepoMetadataDatabaseName)))
            {
                string value;
                if (dictionary.TryGetValue(DiskLayoutVersionKey, out value))
                {
                    return value;
                }

                return null;
            }
        }
    }
}
