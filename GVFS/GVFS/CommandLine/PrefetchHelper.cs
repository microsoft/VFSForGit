using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System.IO;

namespace GVFS.CommandLine
{
    public class PrefetchHelper
    {
        private readonly GitObjects gitObjects;
        
        public PrefetchHelper(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            this.gitObjects = new GitObjects(tracer, enlistment, objectRequestor);
        }

        public bool TryPrefetchCommitsAndTrees()
        {
            string[] packs = this.gitObjects.ReadPackFileNames(GVFSConstants.PrefetchPackPrefix);
            long max = -1;
            foreach (string pack in packs)
            {
                long? timestamp = GetTimestamp(pack);
                if (timestamp.HasValue && timestamp > max)
                {
                    max = timestamp.Value;
                }
            }

            return this.gitObjects.TryDownloadPrefetchPacks(max);
        }

        private static long? GetTimestamp(string packName)
        {
            string filename = Path.GetFileName(packName);
            if (!filename.StartsWith(GVFSConstants.PrefetchPackPrefix))
            {
                return null;
            }

            string[] parts = filename.Split('-');
            long parsed;
            if (parts.Length > 1 && long.TryParse(parts[1], out parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
