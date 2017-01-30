using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.IO;

namespace GVFS.CommandLine
{
    public class PrefetchHelper
    {
        private readonly GitObjects gitObjects;

        public PrefetchHelper(ITracer tracer, GVFSEnlistment enlistment, int downloadThreadCount)
        {
            HttpGitObjects http = new HttpGitObjects(tracer, enlistment, downloadThreadCount);
            this.gitObjects = new GitObjects(tracer, enlistment, http);
        }

        public void PrefetchCommitsAndTrees()
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

            this.gitObjects.DownloadPrefetchPacks(max);
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
