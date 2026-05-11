using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Globalization;
using System.IO;

namespace GVFS.CommandLine
{
    public class CacheVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string CacheVerbName = "cache";

        public CacheVerb()
        {
        }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("cache", "Display information about the GVFS shared object cache");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<CacheVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true);

            return cmd;
        }

        protected override string VerbName
        {
            get { return CacheVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (ITracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "CacheVerb"))
            {
                string localCacheRoot;
                string gitObjectsRoot;
                this.GetLocalCachePaths(tracer, enlistment, out localCacheRoot, out gitObjectsRoot);

                if (string.IsNullOrWhiteSpace(gitObjectsRoot))
                {
                    this.ReportErrorAndExit("Could not determine git objects root. Is this a GVFS enlistment with a shared cache?");
                }

                this.Output.WriteLine("Repo URL:        " + enlistment.RepoUrl);
                this.Output.WriteLine("Cache root:      " + (localCacheRoot ?? "(unknown)"));
                this.Output.WriteLine("Git objects:     " + gitObjectsRoot);

                string packRoot = Path.Combine(gitObjectsRoot, GVFSConstants.DotGit.Objects.Pack.Name);
                if (!Directory.Exists(packRoot))
                {
                    this.Output.WriteLine();
                    this.Output.WriteLine("Pack directory not found: " + packRoot);
                    tracer.RelatedError("Pack directory not found: " + packRoot);
                    return;
                }

                int prefetchPackCount;
                long prefetchPackSize;
                int otherPackCount;
                long otherPackSize;
                long latestPrefetchTimestamp;
                this.GetPackSummary(packRoot, out prefetchPackCount, out prefetchPackSize, out otherPackCount, out otherPackSize, out latestPrefetchTimestamp);

                int looseObjectCount = this.CountLooseObjects(gitObjectsRoot);

                long totalSize = prefetchPackSize + otherPackSize;
                this.Output.WriteLine();
                this.Output.WriteLine("Total pack size: " + this.FormatSizeForUserDisplay(totalSize));
                this.Output.WriteLine("Prefetch packs:  " + prefetchPackCount + " (" + this.FormatSizeForUserDisplay(prefetchPackSize) + ")");
                this.Output.WriteLine("Other packs:     " + otherPackCount + " (" + this.FormatSizeForUserDisplay(otherPackSize) + ")");

                if (latestPrefetchTimestamp > 0)
                {
                    try
                    {
                        DateTimeOffset latestTime = DateTimeOffset.FromUnixTimeSeconds(latestPrefetchTimestamp).ToLocalTime();
                        this.Output.WriteLine("Latest prefetch: " + latestTime.ToString("yyyy-MM-dd HH:mm:ss zzz"));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        tracer.RelatedWarning("Prefetch timestamp out of range: " + latestPrefetchTimestamp);
                    }
                }

                this.Output.WriteLine("Loose objects:   " + looseObjectCount.ToString("N0"));

                EventMetadata metadata = new EventMetadata();
                metadata.Add("repoUrl", enlistment.RepoUrl);
                metadata.Add("localCacheRoot", localCacheRoot);
                metadata.Add("gitObjectsRoot", gitObjectsRoot);
                metadata.Add("prefetchPackCount", prefetchPackCount);
                metadata.Add("prefetchPackSize", prefetchPackSize);
                metadata.Add("otherPackCount", otherPackCount);
                metadata.Add("otherPackSize", otherPackSize);
                metadata.Add("latestPrefetchTimestamp", latestPrefetchTimestamp);
                metadata.Add("looseObjectCount", looseObjectCount);
                tracer.RelatedEvent(EventLevel.Informational, "CacheInfo", metadata, Keywords.Telemetry);
            }
        }

        internal void GetPackSummary(
            string packRoot,
            out int prefetchPackCount,
            out long prefetchPackSize,
            out int otherPackCount,
            out long otherPackSize,
            out long latestPrefetchTimestamp)
        {
            prefetchPackCount = 0;
            prefetchPackSize = 0;
            otherPackCount = 0;
            otherPackSize = 0;
            latestPrefetchTimestamp = 0;

            string[] packFiles = Directory.GetFiles(packRoot, "*.pack");

            foreach (string packFile in packFiles)
            {
                long length;
                try
                {
                    length = new FileInfo(packFile).Length;
                }
                catch (IOException)
                {
                    continue;
                }

                string fileName = Path.GetFileName(packFile);

                if (fileName.StartsWith(GVFSConstants.PrefetchPackPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    prefetchPackCount++;
                    prefetchPackSize += length;

                    long? timestamp = this.TryGetPrefetchTimestamp(packFile);
                    if (timestamp.HasValue && timestamp.Value > latestPrefetchTimestamp)
                    {
                        latestPrefetchTimestamp = timestamp.Value;
                    }
                }
                else
                {
                    otherPackCount++;
                    otherPackSize += length;
                }
            }
        }

        internal int CountLooseObjects(string gitObjectsRoot)
        {
            int looseObjectCount = 0;

            for (int i = 0; i < 256; i++)
            {
                string hexDir = Path.Combine(gitObjectsRoot, i.ToString("x2"));
                if (Directory.Exists(hexDir))
                {
                    try
                    {
                        looseObjectCount += Directory.GetFiles(hexDir).Length;
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            return looseObjectCount;
        }

        private long? TryGetPrefetchTimestamp(string packPath)
        {
            string filename = Path.GetFileName(packPath);
            string[] parts = filename.Split('-');
            if (parts.Length > 1 && long.TryParse(parts[1], out long timestamp))
            {
                return timestamp;
            }

            return null;
        }

        internal string FormatSizeForUserDisplay(long bytes)
        {
            if (bytes >= 1L << 30)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:F1} GB", bytes / (double)(1L << 30));
            }

            if (bytes >= 1L << 20)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:F1} MB", bytes / (double)(1L << 20));
            }

            if (bytes >= 1L << 10)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:F1} KB", bytes / (double)(1L << 10));
            }

            return bytes + " bytes";
        }

        private void GetLocalCachePaths(ITracer tracer, GVFSEnlistment enlistment, out string localCacheRoot, out string gitObjectsRoot)
        {
            localCacheRoot = null;
            gitObjectsRoot = null;

            try
            {
                string error;
                if (RepoMetadata.TryInitialize(tracer, Path.Combine(enlistment.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot), out error))
                {
                    if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
                    {
                        tracer.RelatedWarning("Failed to read local cache root: " + error);
                    }

                    if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
                    {
                        tracer.RelatedWarning("Failed to read git objects root: " + error);
                    }
                }
                else
                {
                    this.ReportErrorAndExit("Failed to read repo metadata: " + error);
                }
            }
            catch (Exception e)
            {
                this.ReportErrorAndExit("Failed to read repo metadata: " + e.Message);
            }
            finally
            {
                RepoMetadata.Shutdown();
            }
        }
    }
}
