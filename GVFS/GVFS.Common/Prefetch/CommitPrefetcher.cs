using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Prefetch
{
    public static class CommitPrefetcher
    {
        private const int IoFailureRetryDelayMS = 50;
        private const int LockWaitTimeMs = 100;
        private const int WaitingOnLockLogThreshold = 50;
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";

        public static bool TryPrefetchCommitsAndTrees(
            ITracer tracer,
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            GitObjects gitObjects,
            out string error)
        {
            List<string> packIndexes;
            using (FileBasedLock prefetchLock = GVFSPlatform.Instance.CreateFileBasedLock(
                fileSystem,
                tracer,
                Path.Combine(enlistment.GitPackRoot, PrefetchCommitsAndTreesLock)))
            {
                WaitUntilLockIsAcquired(tracer, prefetchLock);
                long maxGoodTimeStamp;

                gitObjects.DeleteStaleTempPrefetchPackAndIdxs();

                if (!TryGetMaxGoodPrefetchTimestamp(tracer, enlistment, fileSystem, gitObjects, out maxGoodTimeStamp, out error))
                {
                    return false;
                }

                if (!gitObjects.TryDownloadPrefetchPacks(maxGoodTimeStamp, out packIndexes))
                {
                    error = "Failed to download prefetch packs";
                    return false;
                }
            }

            if (packIndexes?.Count > 0)
            {
                TrySchedulePostFetchJob(tracer, enlistment.NamedPipeName, packIndexes);
            }

            return true;
        }

        public static bool TryGetMaxGoodPrefetchTimestamp(
            ITracer tracer,
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            GitObjects gitObjects,
            out long maxGoodTimestamp,
            out string error)
        {
            fileSystem.CreateDirectory(enlistment.GitPackRoot);

            string[] packs = gitObjects.ReadPackFileNames(enlistment.GitPackRoot, GVFSConstants.PrefetchPackPrefix);
            List<PrefetchPackInfo> orderedPacks = packs
                .Where(pack => GetTimestamp(pack).HasValue)
                .Select(pack => new PrefetchPackInfo(GetTimestamp(pack).Value, pack))
                .OrderBy(packInfo => packInfo.Timestamp)
                .ToList();

            maxGoodTimestamp = -1;

            int firstBadPack = -1;
            for (int i = 0; i < orderedPacks.Count; ++i)
            {
                long timestamp = orderedPacks[i].Timestamp;
                string packPath = orderedPacks[i].Path;
                string idxPath = Path.ChangeExtension(packPath, ".idx");
                if (!fileSystem.FileExists(idxPath))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("pack", packPath);
                    metadata.Add("idxPath", idxPath);
                    metadata.Add("timestamp", timestamp);
                    GitProcess.Result indexResult = gitObjects.IndexPackFile(packPath);
                    if (indexResult.HasErrors)
                    {
                        firstBadPack = i;

                        metadata.Add("Errors", indexResult.Errors);
                        tracer.RelatedWarning(metadata, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and failed to regenerate idx");
                        break;
                    }
                    else
                    {
                        maxGoodTimestamp = timestamp;

                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and regenerated idx");
                        tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_RebuildIdx", metadata);
                    }
                }
                else
                {
                    maxGoodTimestamp = timestamp;
                }
            }

            if (firstBadPack != -1)
            {
                const int MaxDeleteRetries = 200; // 200 * IoFailureRetryDelayMS (50ms) = 10 seconds
                const int RetryLoggingThreshold = 40; // 40 * IoFailureRetryDelayMS (50ms) = 2 seconds

                // Delete packs and indexes in reverse order so that if prefetch is killed, subseqeuent prefetch commands will
                // find the right starting spot.
                for (int i = orderedPacks.Count - 1; i >= firstBadPack; --i)
                {
                    string packPath = orderedPacks[i].Path;
                    string idxPath = Path.ChangeExtension(packPath, ".idx");

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("path", idxPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)} deleting bad idx file");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_DeleteBadIdx", metadata);
                    if (!fileSystem.TryWaitForDelete(tracer, idxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {idxPath}";
                        return false;
                    }

                    metadata = new EventMetadata();
                    metadata.Add("path", packPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)} deleting bad pack file");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_DeleteBadPack", metadata);
                    if (!fileSystem.TryWaitForDelete(tracer, packPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {packPath}";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private static bool TrySchedulePostFetchJob(ITracer tracer, string namedPipeName, List<string> packIndexes)
        {
            // We make a best-effort request to run MIDX and commit-graph writes
            using (NamedPipeClient pipeClient = new NamedPipeClient(namedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    tracer.RelatedWarning(
                        metadata: null,
                        message: "Failed to connect to GVFS. Skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                    return false;
                }

                NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(packIndexes);
                if (pipeClient.TrySendRequest(request.CreateMessage()))
                {
                    NamedPipeMessages.Message response;

                    if (pipeClient.TryReadResponse(out response))
                    {
                        tracer.RelatedInfo("Requested post-fetch job with resonse '{0}'", response.Header);
                        return true;
                    }
                    else
                    {
                        tracer.RelatedWarning(
                            metadata: null,
                            message: "Requested post-fetch job failed to respond",
                            keywords: Keywords.Telemetry);
                    }
                }
                else
                {
                    tracer.RelatedWarning(
                        metadata: null,
                        message: "Message to named pipe failed to send, skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                }
            }

            return false;
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

        private static void WaitUntilLockIsAcquired(ITracer tracer, FileBasedLock fileBasedLock)
        {
            int attempt = 0;
            while (!fileBasedLock.TryAcquireLock())
            {
                Thread.Sleep(LockWaitTimeMs);
                ++attempt;
                if (attempt == WaitingOnLockLogThreshold)
                {
                    attempt = 0;
                    tracer.RelatedInfo("WaitUntilLockIsAcquired: Waiting to acquire prefetch lock");
                }
            }
        }

        private class PrefetchPackInfo
        {
            public PrefetchPackInfo(long timestamp, string path)
            {
                this.Timestamp = timestamp;
                this.Path = path;
            }

            public long Timestamp { get; }
            public string Path { get; }
        }
    }
}
