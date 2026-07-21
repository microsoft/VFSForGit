using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Maintenance
{
    /// <summary>
    /// This step maintains the packfiles in the object cache.
    ///
    /// This is done in two steps:
    ///
    /// git multi-pack-index expire: This deletes the pack-files whose objects
    /// appear in newer pack-files. The multi-pack-index prevents git from
    /// looking at these packs. Rewrites the multi-pack-index to no longer
    /// refer to these (deleted) packs.
    ///
    /// git multi-pack-index repack --batch-size= inspects packs covered by the
    /// multi-pack-index in modified-time order(ascending). Greedily selects a
    /// batch of packs whose file sizes are all less than "size", but that sum
    /// up to at least "size". Then generate a new pack-file containing the
    /// objects that are uniquely referenced by the multi-pack-index.
    /// </summary>
    public class PackfileMaintenanceStep : GitMaintenanceStep
    {
        public const string PackfileLastRunFileName = "pack-maintenance.time";
        public const string DefaultBatchSize = "2g";
        private const string MultiPackIndexLock = "multi-pack-index.lock";
        private readonly bool forceRun;
        private readonly string batchSize;
        private readonly Action requestPrefetch;

        // Set once corrupt packs have been detected and reported with recovery disabled. Recovery leaves
        // the corrupt packs in place, so 'git multi-pack-index write/verify' keeps failing on them for
        // the rest of this maintenance run - once reported, skip re-verifying every pack on each
        // subsequent failure in the same run rather than repeating an identical, already-known result.
        private bool reportedCorruptPacksWithRecoveryDisabled;

        public PackfileMaintenanceStep(
            GVFSContext context,
            bool requireObjectCacheLock = true,
            bool forceRun = false,
            string batchSize = DefaultBatchSize,
            GitProcessChecker gitProcessChecker = null,
            Action requestPrefetch = null)
            : base(context, requireObjectCacheLock, gitProcessChecker)
        {
            this.forceRun = forceRun;
            this.batchSize = batchSize;
            this.requestPrefetch = requestPrefetch;
        }

        public override string Area => nameof(PackfileMaintenanceStep);
        protected override string LastRunTimeFilePath => Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", PackfileLastRunFileName);
        protected override TimeSpan TimeBetweenRuns => TimeSpan.FromDays(1);

        // public only for unit tests
        public List<string> CleanStaleIdxFiles(out int numDeletionBlocked)
        {
            List<DirectoryItemInfo> packDirContents = this.Context
                                                          .FileSystem
                                                          .ItemsInDirectory(this.Context.Enlistment.GitPackRoot)
                                                          .ToList();

            numDeletionBlocked = 0;
            List<string> deletedIdxFiles = new List<string>();

            // If something (probably VFS for Git) has a handle open to a ".idx" file, then
            // the 'git multi-pack-index expire' command cannot delete it. We should come in
            // later and try to clean these up. Count those that we are able to delete and
            // those we still can't.

            foreach (DirectoryItemInfo info in packDirContents)
            {
                if (string.Equals(Path.GetExtension(info.Name), ".idx", GVFSPlatform.Instance.Constants.PathComparison))
                {
                    string pairedPack = Path.ChangeExtension(info.FullName, ".pack");

                    if (!this.Context.FileSystem.FileExists(pairedPack))
                    {
                        if (this.Context.FileSystem.TryDeleteFile(info.FullName))
                        {
                            deletedIdxFiles.Add(info.Name);
                        }
                        else
                        {
                            numDeletionBlocked++;
                        }
                    }
                }
            }

            return deletedIdxFiles;
        }

        protected override void PerformMaintenance()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity(this.Area, EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                // forceRun is only currently true for functional tests
                if (!this.forceRun)
                {
                    if (!this.EnoughTimeBetweenRuns())
                    {
                        activity.RelatedWarning($"Skipping {nameof(PackfileMaintenanceStep)} due to not enough time between runs");
                        return;
                    }

                    IEnumerable<int> processIds = this.GitProcessChecker.GetRunningGitProcessIds();
                    if (processIds.Any())
                    {
                        activity.RelatedWarning($"Skipping {nameof(PackfileMaintenanceStep)} due to git pids {string.Join(",", processIds)}", Keywords.Telemetry);
                        return;
                    }
                }

                this.GetPackFilesInfo(out int beforeCount, out long beforeSize, out bool hasKeep);

                if (!hasKeep)
                {
                    activity.RelatedWarning(this.CreateEventMetadata(), "Skipping pack maintenance due to no .keep file.");
                    return;
                }

                string multiPackIndexLockPath = Path.Combine(this.Context.Enlistment.GitPackRoot, MultiPackIndexLock);
                this.Context.FileSystem.TryDeleteFile(multiPackIndexLockPath);

                // Read the recovery kill switch while the repo is open. When disabled, we still detect and
                // report corrupt packs but do not delete anything.
                bool recoveryEnabled = this.IsPackfileRecoveryEnabled();

                // A corrupt or truncated packfile in the shared object cache (e.g. introduced by a
                // disk-full event) makes 'git multi-pack-index write' fail with "could not load pack N".
                // The existing self-heal only ran after a later verify failed - but the write is first,
                // so recover on the write path too rather than pressing on with a broken cache.
                GitProcess.Result writeResult = this.RunGitCommand((process) => process.WriteMultiPackIndex(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.WriteMultiPackIndex));

                if (!this.Stopping && writeResult.ExitCodeIsFailure)
                {
                    this.RepairMultiPackIndex(activity, writeResult, recoveryEnabled);
                }

                // If a LibGit2Repo is active, then it may hold handles to the .idx and .pack files we want
                // to delete during the 'git multi-pack-index expire' step. If one starts during the step,
                // then it can still block those deletions, but we will clean them up in the next run. By
                // running CloseActiveRepos() here, we ensure that we do not run twice with the same
                // LibGit2Repo active across two calls. A "new" repo should not hold handles to .idx files
                // that do not have corresponding .pack files, so we will clean them up in CleanStaleIdxFiles().
                this.Context.Repository.CloseActiveRepo();

                GitProcess.Result expireResult = this.RunGitCommand((process) => process.MultiPackIndexExpire(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.MultiPackIndexExpire));

                this.Context.Repository.OpenRepo();

                List<string> staleIdxFiles = this.CleanStaleIdxFiles(out int numDeletionBlocked);
                this.GetPackFilesInfo(out int expireCount, out long expireSize, out hasKeep);

                GitProcess.Result verifyAfterExpire = this.RunGitCommand((process) => process.VerifyMultiPackIndex(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyMultiPackIndex));

                if (!this.Stopping && verifyAfterExpire.ExitCodeIsFailure)
                {
                    this.RepairMultiPackIndex(activity, verifyAfterExpire, recoveryEnabled);
                }

                GitProcess.Result repackResult = this.RunGitCommand((process) => process.MultiPackIndexRepack(this.Context.Enlistment.GitObjectsRoot, this.batchSize), nameof(GitProcess.MultiPackIndexRepack));
                this.GetPackFilesInfo(out int afterCount, out long afterSize, out hasKeep);

                GitProcess.Result verifyAfterRepack = this.RunGitCommand((process) => process.VerifyMultiPackIndex(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyMultiPackIndex));

                if (!this.Stopping && verifyAfterRepack.ExitCodeIsFailure)
                {
                    this.RepairMultiPackIndex(activity, verifyAfterRepack, recoveryEnabled);
                }

                EventMetadata metadata = new EventMetadata();
                metadata.Add("GitObjectsRoot", this.Context.Enlistment.GitObjectsRoot);
                metadata.Add("BatchSize", this.batchSize);
                metadata.Add(nameof(beforeCount), beforeCount);
                metadata.Add(nameof(beforeSize), beforeSize);
                metadata.Add(nameof(expireCount), expireCount);
                metadata.Add(nameof(expireSize), expireSize);
                metadata.Add(nameof(afterCount), afterCount);
                metadata.Add(nameof(afterSize), afterSize);
                metadata.Add("VerifyAfterExpireExitCode", verifyAfterExpire.ExitCode);
                metadata.Add("VerifyAfterRepackExitCode", verifyAfterRepack.ExitCode);
                metadata.Add("NumStaleIdxFiles", staleIdxFiles.Count);
                metadata.Add("NumIdxDeletionsBlocked", numDeletionBlocked);
                activity.RelatedEvent(EventLevel.Informational, $"{this.Area}_{nameof(this.PerformMaintenance)}", metadata, Keywords.Telemetry);

                this.SaveLastRunTimeToFile();
            }
        }

        /// <summary>
        /// Reads the <c>gvfs.enable-packfile-recovery</c> kill switch. Virtual so unit tests can
        /// override it; the LibGit2 invoker is null in tests, in which case recovery defaults to enabled.
        /// </summary>
        protected virtual bool IsPackfileRecoveryEnabled()
        {
            LibGit2RepoInvoker repoInvoker = this.Context.Repository.LibGit2RepoInvoker;
            if (repoInvoker == null)
            {
                return GVFSConstants.GitConfig.EnablePackfileRecoveryDefault;
            }

            return repoInvoker.GetConfigBoolOrDefault(
                GVFSConstants.GitConfig.EnablePackfileRecovery,
                GVFSConstants.GitConfig.EnablePackfileRecoveryDefault);
        }

        private static bool ResultIndicatesCorruptPack(GitProcess.Result result)        {
            string errors = result?.Errors;
            if (string.IsNullOrEmpty(errors))
            {
                return false;
            }

            // 'git multi-pack-index write/verify' reports an unreadable underlying packfile with
            // messages like "could not load pack N" or "failed to load pack in position N". Both mean
            // a packfile - not just the multi-pack-index - is corrupt.
            return errors.IndexOf("could not load pack", StringComparison.OrdinalIgnoreCase) >= 0
                || errors.IndexOf("failed to load pack", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns the prefetch timestamp encoded in a prefetch pack file name
        /// (prefetch-&lt;timestamp&gt;-&lt;uniqueId&gt;.pack), or null if the file is not a prefetch pack.
        /// </summary>
        private static long? GetPrefetchTimestamp(string packFileName)
        {
            if (!packFileName.StartsWith(GVFSConstants.PrefetchPackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string[] parts = packFileName.Split('-');
            if (parts.Length > 1 && long.TryParse(parts[1], out long timestamp))
            {
                return timestamp;
            }

            return null;
        }

        /// <summary>
        /// Recovers from a failed multi-pack-index write or verify. When git reports it could not load a
        /// pack, a packfile itself is corrupt (e.g. truncated by a past disk-full event) and regenerating
        /// the multi-pack-index (MIDX) alone keeps failing because the rewrite re-scans the same bad pack.
        /// Detect the corrupt pack(s) - and, when recovery is enabled, remove them - then delete and
        /// regenerate the MIDX from the packs that remain (fast path, no full repack).
        /// </summary>
        private void RepairMultiPackIndex(ITracer activity, GitProcess.Result failure, bool recoveryEnabled)
        {
            bool prefetchRestoreNeeded = false;

            if (!this.Stopping && ResultIndicatesCorruptPack(failure))
            {
                this.DetectAndRemoveCorruptPacks(activity, recoveryEnabled, out prefetchRestoreNeeded);
            }

            // Delete the (now stale) multi-pack-index and rebuild it from the packs that remain. This is
            // non-destructive and runs regardless of the recovery kill switch.
            this.LogErrorAndRewriteMultiPackIndex(activity);

            if (prefetchRestoreNeeded && !this.Stopping)
            {
                this.RequestPrefetchRestore(activity);
            }
        }

        /// <summary>
        /// Verifies each packfile in the object cache with 'git verify-pack' and reports every unreadable
        /// pack via telemetry (this detection runs even when recovery is disabled). When
        /// <paramref name="recoveryEnabled"/> is true, also removes each corrupt pack's files
        /// (.pack/.idx/.keep/.rev). A corrupt <em>prefetch</em> pack additionally forces removal of every
        /// later (higher-timestamp) prefetch pack and sets <paramref name="prefetchRestoreNeeded"/>,
        /// because prefetch packs are incremental - leaving a hole would let the newest surviving
        /// timestamp advance past it so a later prefetch never backfills the gap.
        /// </summary>
        // public only for unit tests
        public void DetectAndRemoveCorruptPacks(ITracer activity, bool recoveryEnabled, out bool prefetchRestoreNeeded)
        {
            prefetchRestoreNeeded = false;

            if (!recoveryEnabled && this.reportedCorruptPacksWithRecoveryDisabled)
            {
                // Already verified every pack and reported the corrupt ones earlier in this maintenance
                // run. Recovery is disabled, so nothing has changed on disk - skip the redundant rescan.
                return;
            }

            List<DirectoryItemInfo> packDirContents = this.Context
                                                          .FileSystem
                                                          .ItemsInDirectory(this.Context.Enlistment.GitPackRoot)
                                                          .ToList();

            // Phase 1 - detection (read-only, always runs). Verify each pack that exists on disk and
            // report the corrupt ones. verify-pack is an external git process, so it is safe to run with
            // the LibGit2 repo open.
            long? minCorruptPrefetchTimestamp = null;
            List<string> corruptNonPrefetchIdxPaths = new List<string>();
            HashSet<string> corruptIdxPaths = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);

            foreach (DirectoryItemInfo info in packDirContents)
            {
                if (this.Stopping)
                {
                    return;
                }

                if (!string.Equals(Path.GetExtension(info.Name), ".idx", GVFSPlatform.Instance.Constants.PathComparison))
                {
                    continue;
                }

                string idxPath = info.FullName;
                string packPath = Path.ChangeExtension(idxPath, ".pack");

                // A dangling .idx with no matching .pack is handled by CleanStaleIdxFiles; here we only
                // care about packs that exist on disk but cannot be read.
                if (!this.Context.FileSystem.FileExists(packPath))
                {
                    continue;
                }

                GitProcess.Result verifyPackResult = this.RunGitCommand((process) => process.VerifyPack(idxPath), nameof(GitProcess.VerifyPack));

                if (this.Stopping)
                {
                    return;
                }

                if (verifyPackResult.ExitCodeIsSuccess)
                {
                    continue;
                }

                long? prefetchTimestamp = GetPrefetchTimestamp(info.Name);
                bool isPrefetchPack = prefetchTimestamp.HasValue;
                corruptIdxPaths.Add(idxPath);

                EventMetadata foundMetadata = this.CreateEventMetadata();
                foundMetadata["Operation"] = "FoundCorruptPack";
                foundMetadata["Pack"] = info.Name;
                foundMetadata["IsPrefetchPack"] = isPrefetchPack;
                foundMetadata["RecoveryEnabled"] = recoveryEnabled;
                activity.RelatedWarning(foundMetadata, $"Found corrupt packfile {info.Name} during pack maintenance.", Keywords.Telemetry);

                if (isPrefetchPack)
                {
                    if (!minCorruptPrefetchTimestamp.HasValue || prefetchTimestamp.Value < minCorruptPrefetchTimestamp.Value)
                    {
                        minCorruptPrefetchTimestamp = prefetchTimestamp.Value;
                    }
                }
                else
                {
                    corruptNonPrefetchIdxPaths.Add(idxPath);
                }
            }

            if (corruptIdxPaths.Count == 0)
            {
                return;
            }

            if (!recoveryEnabled)
            {
                EventMetadata skippedMetadata = this.CreateEventMetadata();
                skippedMetadata["Operation"] = "CorruptPackRecoverySkipped";
                skippedMetadata["CorruptPackCount"] = corruptIdxPaths.Count;
                activity.RelatedWarning(
                    skippedMetadata,
                    $"Found {corruptIdxPaths.Count} corrupt packfile(s) but {GVFSConstants.GitConfig.EnablePackfileRecovery} is disabled; leaving packs in place.",
                    Keywords.Telemetry);
                this.reportedCorruptPacksWithRecoveryDisabled = true;
                return;
            }

            // Phase 2 - deletion (gated). Build the set of prefetch packs to remove: the corrupt one and
            // every later (>= timestamp) prefetch pack, whether or not those later packs are themselves
            // corrupt, because prefetch packs are incremental.
            List<string> laterPrefetchIdxPaths = new List<string>();
            if (minCorruptPrefetchTimestamp.HasValue)
            {
                foreach (DirectoryItemInfo info in packDirContents)
                {
                    if (!string.Equals(Path.GetExtension(info.Name), ".pack", GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        continue;
                    }

                    long? prefetchTimestamp = GetPrefetchTimestamp(info.Name);
                    if (prefetchTimestamp.HasValue && prefetchTimestamp.Value >= minCorruptPrefetchTimestamp.Value)
                    {
                        laterPrefetchIdxPaths.Add(Path.ChangeExtension(info.FullName, ".idx"));
                    }
                }
            }

            // Only request a prefetch restore once a corrupt prefetch pack is actually removed. If
            // deletion is blocked (e.g. a handle is still open), the corrupt pack is still present, so
            // running the restore now would just re-download around a cache that is still broken.
            bool corruptPrefetchPackRemoved = false;

            // Close the LibGit2 repo so the .idx files can be deleted, then remove each pack set.
            this.Context.Repository.CloseActiveRepo();
            try
            {
                foreach (string idxPath in corruptNonPrefetchIdxPaths)
                {
                    if (this.Stopping)
                    {
                        return;
                    }

                    this.RemovePackFileSet(activity, idxPath, "DeletedCorruptPack", $"Deleted corrupt packfile {Path.GetFileName(Path.ChangeExtension(idxPath, ".pack"))} during pack maintenance recovery.");
                }

                foreach (string idxPath in laterPrefetchIdxPaths)
                {
                    if (this.Stopping)
                    {
                        return;
                    }

                    if (corruptIdxPaths.Contains(idxPath))
                    {
                        bool removed = this.RemovePackFileSet(activity, idxPath, "DeletedCorruptPack", $"Deleted corrupt prefetch packfile {Path.GetFileName(Path.ChangeExtension(idxPath, ".pack"))} during pack maintenance recovery.");
                        corruptPrefetchPackRemoved = corruptPrefetchPackRemoved || removed;
                    }
                    else
                    {
                        this.RemovePackFileSet(activity, idxPath, "DeletedHealthyPrefetchPack", $"Deleted healthy prefetch packfile {Path.GetFileName(Path.ChangeExtension(idxPath, ".pack"))} because an earlier prefetch pack was corrupt; incremental prefetch packs after the corruption must be removed and re-fetched.");
                    }
                }
            }
            finally
            {
                this.Context.Repository.OpenRepo();
            }

            prefetchRestoreNeeded = corruptPrefetchPackRemoved;
        }

        /// <returns>
        /// True if the packfile itself was deleted. The .pack file is what actually contains the corrupt
        /// (or, for a later prefetch pack, stale) data, so its deletion result - not the sidecar
        /// .idx/.keep/.rev files - is what determines whether recovery for this pack set succeeded.
        /// </returns>
        private bool RemovePackFileSet(ITracer activity, string idxPath, string operation, string message)
        {
            string packPath = Path.ChangeExtension(idxPath, ".pack");
            bool packDeleted = this.Context.FileSystem.TryDeleteFile(packPath);

            EventMetadata metadata = this.CreateEventMetadata();
            metadata["Operation"] = operation;
            metadata["Pack"] = Path.GetFileName(packPath);
            metadata["DeletePackResult"] = packDeleted;
            metadata["DeleteIdxResult"] = this.Context.FileSystem.TryDeleteFile(idxPath);
            metadata["DeleteKeepResult"] = this.Context.FileSystem.TryDeleteFile(Path.ChangeExtension(idxPath, ".keep"));
            metadata["DeleteRevResult"] = this.Context.FileSystem.TryDeleteFile(Path.ChangeExtension(idxPath, ".rev"));
            activity.RelatedWarning(metadata, message, Keywords.Telemetry);

            return packDeleted;
        }

        private void RequestPrefetchRestore(ITracer activity)
        {
            if (this.requestPrefetch == null)
            {
                // No prefetch is available (e.g. not using a cache server). The removed prefetch packs'
                // objects will be re-fetched on demand through normal virtualization.
                EventMetadata metadata = this.CreateEventMetadata();
                metadata["Operation"] = "PrefetchRestoreUnavailable";
                activity.RelatedWarning(
                    metadata,
                    "Removed prefetch pack(s) but no prefetch restore is available. Missing objects will be re-fetched on demand.",
                    Keywords.Telemetry);
                return;
            }

            activity.RelatedInfo("Requesting a prefetch to restore removed prefetch packs and rebuild the commit-graph.");
            this.requestPrefetch();
        }
    }
}
