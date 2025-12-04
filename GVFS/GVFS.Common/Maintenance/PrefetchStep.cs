using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common.Maintenance
{
    public class PrefetchStep : GitMaintenanceStep
    {
        private const int IoFailureRetryDelayMS = 50;
        private const int LockWaitTimeMs = 100;
        private const int WaitingOnLockLogThreshold = 50;
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";
        private const int NoExistingPrefetchPacks = -1;
        private readonly TimeSpan timeBetweenPrefetches = TimeSpan.FromMinutes(70);

        public PrefetchStep(GVFSContext context, GitObjects gitObjects, bool requireCacheLock = true)
            : base(context, requireCacheLock)
        {
            this.GitObjects = gitObjects;
        }

        public override string Area => "PrefetchStep";

        protected GitObjects GitObjects { get; }

        public bool TryPrefetchCommitsAndTrees(out string error, GitProcess gitProcess = null)
        {
            if (gitProcess == null)
            {
                gitProcess = new GitProcess(this.Context.Enlistment);
            }

            List<string> packIndexes;

            // We take our own lock here to keep background and foreground prefetches
            // from running at the same time.
            using (FileBasedLock prefetchLock = GVFSPlatform.Instance.CreateFileBasedLock(
                this.Context.FileSystem,
                this.Context.Tracer,
                Path.Combine(this.Context.Enlistment.GitPackRoot, PrefetchCommitsAndTreesLock)))
            {
                WaitUntilLockIsAcquired(this.Context.Tracer, prefetchLock);
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

                this.UpdateKeepPacks();
            }

            this.SchedulePostFetchJob(packIndexes);

            return true;
        }

        protected override void PerformMaintenance()
        {
            long last;
            string error = null;

            if (!this.TryGetMaxGoodPrefetchTimestamp(out last, out error))
            {
                this.Context.Tracer.RelatedError(error);
                return;
            }

            if (last == NoExistingPrefetchPacks)
            {
                /* If there are no existing prefetch packs, that means that either the
                 * first prefetch is still in progress or the clone was run with "--no-prefetch".
                 * In either case, we should not run prefetch as a maintenance task.
                 * If users want to prefetch after cloning with "--no-prefetch", they can run
                 * "gvfs prefetch" manually. Also, "git pull" and "git fetch" will run prefetch
                 * as a pre-command hook. */
                this.Context.Tracer.RelatedInfo(this.Area + ": Skipping prefetch since there are no existing prefetch packs");
                return;
            }

            DateTime lastDateTime = EpochConverter.FromUnixEpochSeconds(last);
            DateTime now = DateTime.UtcNow;

            if (now <= lastDateTime + this.timeBetweenPrefetches)
            {
                this.Context.Tracer.RelatedInfo(this.Area + ": Skipping prefetch since most-recent prefetch ({0}) is too close to now ({1})", lastDateTime, now);
                return;
            }

            this.RunGitCommand(
                process =>
                {
                    this.TryPrefetchCommitsAndTrees(out error, process);
                    return null;
                },
                nameof(this.TryPrefetchCommitsAndTrees));

            if (!string.IsNullOrEmpty(error))
            {
                this.Context.Tracer.RelatedWarning(
                    metadata: this.CreateEventMetadata(),
                    message: $"{nameof(this.TryPrefetchCommitsAndTrees)} failed with error '{error}'",
                    keywords: Keywords.Telemetry);
            }
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

        private bool TryGetMaxGoodPrefetchTimestamp(out long maxGoodTimestamp, out string error)
        {
            this.Context.FileSystem.CreateDirectory(this.Context.Enlistment.GitPackRoot);

            string[] packs = this.GitObjects.ReadPackFileNames(this.Context.Enlistment.GitPackRoot, GVFSConstants.PrefetchPackPrefix);
            List<PrefetchPackInfo> orderedPacks = packs
                .Where(pack => GetTimestamp(pack).HasValue)
                .Select(pack => new PrefetchPackInfo(GetTimestamp(pack).Value, pack))
                .OrderBy(packInfo => packInfo.Timestamp)
                .ToList();

            maxGoodTimestamp = NoExistingPrefetchPacks;

            int firstBadPack = -1;
            for (int i = 0; i < orderedPacks.Count; ++i)
            {
                long timestamp = orderedPacks[i].Timestamp;
                string packPath = orderedPacks[i].Path;
                string idxPath = Path.ChangeExtension(packPath, ".idx");
                if (!this.Context.FileSystem.FileExists(idxPath))
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata.Add("pack", packPath);
                    metadata.Add("idxPath", idxPath);
                    metadata.Add("timestamp", timestamp);
                    GitProcess.Result indexResult = this.RunGitCommand(process => this.GitObjects.IndexPackFile(packPath, process), nameof(this.GitObjects.IndexPackFile));

                    if (indexResult.ExitCodeIsFailure)
                    {
                        firstBadPack = i;

                        this.Context.Tracer.RelatedWarning(metadata, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and failed to regenerate idx");
                        break;
                    }
                    else
                    {
                        maxGoodTimestamp = timestamp;

                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}: Found pack file that's missing idx file, and regenerated idx");
                        this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}_RebuildIdx", metadata);
                    }
                }
                else
                {
                    maxGoodTimestamp = timestamp;
                }
            }

            if (this.Stopping)
            {
                throw new StoppingException();
            }

            if (firstBadPack != -1)
            {
                const int MaxDeleteRetries = 200; // 200 * IoFailureRetryDelayMS (50ms) = 10 seconds
                const int RetryLoggingThreshold = 40; // 40 * IoFailureRetryDelayMS (50ms) = 2 seconds

                // Before we delete _any_ pack-files, we need to delete the multi-pack-index, which
                // may refer to those packs.

                EventMetadata metadata = this.CreateEventMetadata();
                string midxPath = Path.Combine(this.Context.Enlistment.GitPackRoot, "multi-pack-index");
                metadata.Add("path", midxPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)} deleting multi-pack-index");
                this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}_DeleteMultiPack_index", metadata);

                if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, midxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                {
                    error = $"Unable to delete {midxPath}";
                    return false;
                }

                // Delete packs and indexes in reverse order so that if prefetch is killed, subseqeuent prefetch commands will
                // find the right starting spot.
                for (int i = orderedPacks.Count - 1; i >= firstBadPack; --i)
                {
                    if (this.Stopping)
                    {
                        throw new StoppingException();
                    }

                    string packPath = orderedPacks[i].Path;
                    string idxPath = Path.ChangeExtension(packPath, ".idx");

                    metadata = this.CreateEventMetadata();
                    metadata.Add("path", idxPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)} deleting bad idx file");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}_DeleteBadIdx", metadata);

                    // We need to close the LibGit2 repo data in order to delete .idx files.
                    // Close inside the loop to only close if necessary, reopen outside the loop
                    // to minimize initializations.
                    this.Context.Repository.CloseActiveRepo();

                    if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, idxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {idxPath}";
                        return false;
                    }

                    metadata = this.CreateEventMetadata();
                    metadata.Add("path", packPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)} deleting bad pack file");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryGetMaxGoodPrefetchTimestamp)}_DeleteBadPack", metadata);

                    if (!this.Context.FileSystem.TryWaitForDelete(this.Context.Tracer, packPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {packPath}";
                        return false;
                    }
                }

                this.Context.Repository.OpenRepo();
            }

            error = null;
            return true;
        }

        private void SchedulePostFetchJob(List<string> packIndexes)
        {
            if (packIndexes.Count == 0)
            {
                return;
            }

            // We make a best-effort request to run MIDX and commit-graph writes
            using (NamedPipeClient pipeClient = new NamedPipeClient(this.Context.Enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: this.CreateEventMetadata(),
                        message: "Failed to connect to GVFS.Mount process. Skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                    return;
                }

                NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(packIndexes);
                if (pipeClient.TrySendRequest(request.CreateMessage()))
                {
                    NamedPipeMessages.Message response;

                    if (pipeClient.TryReadResponse(out response))
                    {
                        this.Context.Tracer.RelatedInfo("Requested post-fetch job with resonse '{0}'", response.Header);
                    }
                    else
                    {
                        this.Context.Tracer.RelatedWarning(
                            metadata: this.CreateEventMetadata(),
                            message: "Requested post-fetch job failed to respond",
                            keywords: Keywords.Telemetry);
                    }
                }
                else
                {
                    this.Context.Tracer.RelatedWarning(
                        metadata: this.CreateEventMetadata(),
                        message: "Message to named pipe failed to send, skipping post-fetch job request.",
                        keywords: Keywords.Telemetry);
                }
            }
        }

        /// <summary>
        /// Ensure the prefetch pack with most-recent timestamp has an associated
        /// ".keep" file. This prevents any Git command from deleting the pack.
        ///
        /// Delete the previous ".keep" file(s) so that pack can be deleted when they
        /// are not the most-recent pack.
        /// </summary>
        private void UpdateKeepPacks()
        {
            if (!this.TryGetMaxGoodPrefetchTimestamp(out long maxGoodTimeStamp, out string error))
            {
                return;
            }

            string prefix = $"prefetch-{maxGoodTimeStamp}-";

            DirectoryItemInfo info = this.Context
                                         .FileSystem
                                         .ItemsInDirectory(this.Context.Enlistment.GitPackRoot)
                                         .Where(item => item.Name.StartsWith(prefix)
                                                        && string.Equals(Path.GetExtension(item.Name), ".pack", GVFSPlatform.Instance.Constants.PathComparison))
                                         .FirstOrDefault();
            if (info == null)
            {
                this.Context.Tracer.RelatedWarning(this.CreateEventMetadata(), $"Could not find latest prefetch pack, starting with {prefix}");
                return;
            }

            string newKeepFile = Path.ChangeExtension(info.FullName, ".keep");

            if (!this.Context.FileSystem.TryWriteAllText(newKeepFile, string.Empty))
            {
                this.Context.Tracer.RelatedWarning(this.CreateEventMetadata(), $"Failed to create .keep file at {newKeepFile}");
                return;
            }

            foreach (string keepFile in this.Context
                                     .FileSystem
                                     .ItemsInDirectory(this.Context.Enlistment.GitPackRoot)
                                     .Where(item => item.Name.EndsWith(".keep"))
                                     .Select(item => item.FullName))
            {
                if (!keepFile.Equals(newKeepFile))
                {
                    this.Context.FileSystem.TryDeleteFile(keepFile);
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
