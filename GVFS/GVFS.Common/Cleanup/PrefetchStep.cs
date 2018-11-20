using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Cleanup
{
    public class PrefetchStep : GitCleanupStep
    {
        private const int IoFailureRetryDelayMS = 50;
        private const int LockWaitTimeMs = 100;
        private const int WaitingOnLockLogThreshold = 50;
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";
        private readonly TimeSpan timeBetweenPrefetches = TimeSpan.FromMinutes(70);

        public PrefetchStep(GVFSContext context, GitObjects gitObjects)
            : base(context, gitObjects)
        {
        }

        public override string TelemetryKey => "PrefetchStep";

        public bool TryPrefetchCommitsAndTrees(out string error, GitProcess gitProcess = null)
        {
            if (gitProcess == null)
            {
                gitProcess = new GitProcess(this.Context.Enlistment);
            }

            List<string> packIndexes;
            using (FileBasedLock prefetchLock = GVFSPlatform.Instance.CreateFileBasedLock(
                this.Context.FileSystem,
                this.Context.Tracer,
                Path.Combine(this.Context.Enlistment.GitPackRoot, PrefetchCommitsAndTreesLock)))
            {
                this.WaitUntilLockIsAcquired(this.Context.Tracer, prefetchLock);
                long maxGoodTimeStamp;

                this.GitObjects.DeleteStaleTempPrefetchPackAndIdxs();
                this.GitObjects.DeleteTemporaryFiles();

                if (!this.TryGetMaxGoodPrefetchTimestamp(out maxGoodTimeStamp, out error))
                {
                    return false;
                }

                if (!this.GitObjects.TryDownloadPrefetchPacks(gitProcess, maxGoodTimeStamp, out packIndexes))
                {
                    error = "Failed to download prefetch packs";
                    return false;
                }
            }

            if (packIndexes?.Count > 0)
            {
                this.TrySchedulePostFetchJob(packIndexes);
            }

            return true;
        }

        protected override void RunGitAction()
        {
            long last;
            string error = null;

            if (!this.TryGetMaxGoodPrefetchTimestamp(out last, out error))
            {
                this.Context.Tracer.RelatedError(error);
                return;
            }

            DateTime lastDateTime = EpochConverter.FromUnixEpochSeconds(last);
            DateTime now = DateTime.UtcNow;

            if (now <= lastDateTime + this.timeBetweenPrefetches)
            {
                this.Context.Tracer.RelatedInfo(this.TelemetryKey + ": Skipping prefetch since most-recent prefetch ({0}) is too close to now ({1})", lastDateTime, now);
                return;
            }

            this.RunGitCommand(process =>
            {
                this.TryPrefetchCommitsAndTrees(out error, process);
                return null;
            });

            if (!string.IsNullOrEmpty(error))
            {
                this.Context.Tracer.RelatedWarning(
                    metadata: null,
                    message: $"{this.TelemetryKey}: {nameof(this.TryPrefetchCommitsAndTrees)} failed with error '{error}'",
                    keywords: Keywords.Telemetry);
            }
        }

        private bool TryGetMaxGoodPrefetchTimestamp(out long maxGoodTimestamp, out string error)
        {
            this.Context.FileSystem.CreateDirectory(this.Context.Enlistment.GitPackRoot);

            string[] packs = GitObjects.ReadPackFileNames(this.Context.Enlistment.GitPackRoot, GVFSConstants.PrefetchPackPrefix);
            List<PrefetchPackInfo> orderedPacks = packs
                .Where(pack => this.GetTimestamp(pack).HasValue)
                .Select(pack => new PrefetchPackInfo(this.GetTimestamp(pack).Value, pack))
                .OrderBy(packInfo => packInfo.Timestamp)
                .ToList();

            maxGoodTimestamp = -1;

            int firstBadPack = -1;
            for (int i = 0; !this.Stopping && i < orderedPacks.Count; ++i)
            {
                long timestamp = orderedPacks[i].Timestamp;
                string packPath = orderedPacks[i].Path;
                string idxPath = Path.ChangeExtension(packPath, ".idx");
                if (!this.Context.FileSystem.FileExists(idxPath))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("pack", packPath);
                    metadata.Add("idxPath", idxPath);
                    metadata.Add("timestamp", timestamp);
                    GitProcess.Result indexResult = this.RunGitCommand(process => this.GitObjects.IndexPackFile(packPath, process));

                    if (this.Stopping)
                    {
                        error = nameof(this.Stopping);
                        return false;
                    }

                    if (indexResult.ExitCodeIsFailure)
                    {
                        firstBadPack = i;

                        metadata.Add("Errors", indexResult.Errors);
                        this.Context.Tracer.RelatedWarning(metadata, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and failed to regenerate idx");
                        break;
                    }
                    else
                    {
                        maxGoodTimestamp = timestamp;

                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and regenerated idx");
                        this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_RebuildIdx", metadata);
                    }
                }
                else
                {
                    maxGoodTimestamp = timestamp;
                }
            }

            if (!this.Stopping && firstBadPack != -1)
            {
                const int MaxDeleteRetries = 200; // 200 * IoFailureRetryDelayMS (50ms) = 10 seconds
                const int RetryLoggingThreshold = 40; // 40 * IoFailureRetryDelayMS (50ms) = 2 seconds

                // Before we delete _any_ pack-files, we need to delete the multi-pack-index, which
                // may refer to those packs.

                EventMetadata metadata = new EventMetadata();
                string midxPath = Path.Combine(this.Context.Enlistment.GitPackRoot, "multi-pack-index");
                metadata.Add("path", midxPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)} deleting multi-pack-index");
                this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_DeleteMultiPack_index", metadata);

                if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, midxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                {
                    error = $"Unable to delete {midxPath}";
                    return false;
                }

                // Delete packs and indexes in reverse order so that if prefetch is killed, subseqeuent prefetch commands will
                // find the right starting spot.
                for (int i = orderedPacks.Count - 1; !this.Stopping && i >= firstBadPack; --i)
                {
                    string packPath = orderedPacks[i].Path;
                    string idxPath = Path.ChangeExtension(packPath, ".idx");

                    metadata = new EventMetadata();
                    metadata.Add("path", idxPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)} deleting bad idx file");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_DeleteBadIdx", metadata);

                    if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, idxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {idxPath}";
                        return false;
                    }

                    metadata = new EventMetadata();
                    metadata.Add("path", packPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(TryGetMaxGoodPrefetchTimestamp)} deleting bad pack file");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryGetMaxGoodPrefetchTimestamp)}_DeleteBadPack", metadata);

                    if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, packPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {packPath}";
                        return false;
                    }
                }
            }

            if (this.Stopping)
            {
                error = nameof(this.Stopping);
                return false;
            }

            error = null;
            return true;
        }

        private bool TrySchedulePostFetchJob(List<string> packIndexes)
        {
            // We make a best-effort request to run MIDX and commit-graph writes
            using (NamedPipeClient pipeClient = new NamedPipeClient(this.Context.Enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: null,
                        message: "Failed to connect to GVFS.Mount process. Skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                    return false;
                }

                NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(packIndexes);
                if (pipeClient.TrySendRequest(request.CreateMessage()))
                {
                    NamedPipeMessages.Message response;

                    if (pipeClient.TryReadResponse(out response))
                    {
                        this.Context.Tracer.RelatedInfo("Requested post-fetch job with resonse '{0}'", response.Header);
                        return true;
                    }
                    else
                    {
                        this.Context.Tracer.RelatedWarning(
                            metadata: null,
                            message: "Requested post-fetch job failed to respond",
                            keywords: Keywords.Telemetry);
                    }
                }
                else
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: null,
                        message: "Message to named pipe failed to send, skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                }
            }

            return false;
        }

        private long? GetTimestamp(string packName)
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

        private void WaitUntilLockIsAcquired(ITracer tracer, FileBasedLock fileBasedLock)
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
