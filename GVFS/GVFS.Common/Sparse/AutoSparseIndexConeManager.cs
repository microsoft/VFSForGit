using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Applies the cone widen and narrow requests that the Git hooks send to the mount when
    /// <c>gvfs.auto-sparse-index</c> is true.
    /// </summary>
    /// <remarks>
    /// The cone is computed from the modified paths plus the transient paths of every Git
    /// command that widened and did not narrow yet (see <see cref="TransientConeState"/>).
    /// When the last command narrows, the cone is again the cone of the modified paths.
    /// <para>
    /// Widen: write the new cone file, then run <see cref="IConeManagerEnvironment.ApplyConeToIndex"/>
    /// so that the index has entries for the new directories before Git runs. If Git fails,
    /// restore the previous cone file. Only the Git command that holds the GVFS lock can
    /// widen: it waits in its pre-command hook, so no other Git command writes the index.
    /// The cone does not include the directory of a deleted file that a sparse-directory entry
    /// hides (see <see cref="SparseDirectoryExpansionGuard"/>).
    /// </para>
    /// <para>
    /// Narrow: write the new cone file only. The narrow arrives after the post-command hook
    /// released the GVFS lock, so the mount cannot replace the index (see
    /// <see cref="IConeManagerEnvironment.CanWriteIndex"/>). The index keeps the expanded
    /// entries, all skip-worktree, until the next widen collapses every directory outside the
    /// cone.
    /// </para>
    /// <para>
    /// Locks: a process-internal lock serializes all requests. Then the manager takes the lock
    /// file of Git's own <c>sparse-checkout</c> writers, writes the cone file, and runs the
    /// child Git process, which takes the index lock. The manager does not take the GVFS lock,
    /// because the requesting Git command holds it. The manager does not take the projection
    /// lock. A cone change only changes how the index represents a directory, so the projection
    /// does not change, and the manager does not invalidate it.
    /// </para>
    /// <para>
    /// Every failure lets Git run, because the hook treats any reply other than success as
    /// "not applied". Git then expands the index in memory, which is slower but correct.
    /// </para>
    /// </remarks>
    public class AutoSparseIndexConeManager
    {
        /// <summary>A widen with more paths is refused, and Git expands the index in memory.</summary>
        public const int MaxWidenPaths = 1000;

        private const string WidenEventName = "ConeWiden";
        private const string NarrowEventName = "ConeNarrow";
        private const int MaxTracedPaths = 10;

        // A widen that waited longer than this for earlier requests does not start Git: the hook
        // stops its wait at WidenWaitBudgetMs, and Git then needs the index lock that the child
        // Git process holds.
        private const int MaxQueueWaitBeforeApplyMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs / 2;

        private readonly ITracer tracer;
        private readonly PhysicalFileSystem fileSystem;
        private readonly ConeFileWriter coneFileWriter;
        private readonly string sparseCheckoutPath;
        private readonly bool ignoreCase;
        private readonly IConeManagerEnvironment environment;
        private readonly TransientConeState transientState = new TransientConeState();
        private readonly Lock coneLock = new Lock();

        // True when the index can have sparse-directory entries that do not match the cone
        // file: before the first widen of this mount, after a narrow, and after a failure.
        private bool indexMayDifferFromCone = true;

        // True when the cone file can differ from the cone of the current state: before the
        // first request of this mount, and after a failed narrow. The next request then
        // writes the cone file even when no command entry changed.
        private bool coneFileMayBeStale = true;

        // The deleted files and modified folders that the last widen found inside a
        // sparse-directory entry. The cone leaves them out, so that a narrow does not put them
        // in the cone file either.
        private HashSet<string> hiddenModifiedPaths = new HashSet<string>(StringComparer.Ordinal);

        public AutoSparseIndexConeManager(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string sparseCheckoutPath,
            bool ignoreCase,
            IConeManagerEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(fileSystem);
            ArgumentNullException.ThrowIfNull(sparseCheckoutPath);
            ArgumentNullException.ThrowIfNull(environment);

            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.coneFileWriter = new ConeFileWriter(fileSystem);
            this.sparseCheckoutPath = sparseCheckoutPath;
            this.ignoreCase = ignoreCase;
            this.environment = environment;
        }

        private enum ConeChange
        {
            Applied,
            ConeUnchanged,
            Narrowed,
            NothingToNarrow,
            NoCommandId,
            InvalidRequest,
            TooManyPaths,
            NotLockHolder,
            CommandEnded,
            CommandAlreadyNarrowed,
            InvalidPath,
            QueueWaitTooLong,
            ExpandsModifiedPath,
            SparseDirectoriesUnknown,
            ConeFileLocked,
            WriteFailed,
            ApplyFailed,
            UnexpectedException,
        }

        /// <summary>
        /// Handle a <see cref="NamedPipeMessages.ConeManagement.WidenRequest"/> or
        /// <see cref="NamedPipeMessages.ConeManagement.NarrowRequest"/> and return the reply.
        /// </summary>
        /// <param name="manager">The cone manager, or null when the feature is off.</param>
        /// <param name="tracer">Traces a request that is not valid.</param>
        /// <param name="message">The request.</param>
        public static string HandleRequest(AutoSparseIndexConeManager manager, ITracer tracer, NamedPipeMessages.Message message)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(message);

            if (manager == null)
            {
                return NamedPipeMessages.ConeManagement.NotEnabledResult;
            }

            bool succeeded;
            switch (message.Header)
            {
                case NamedPipeMessages.ConeManagement.WidenRequest:
                    if (!NamedPipeMessages.ConeManagement.WidenParameters.TryParse(message.Body, out NamedPipeMessages.ConeManagement.WidenParameters widen))
                    {
                        TraceInvalidRequest(tracer, message.Header);
                        return NamedPipeMessages.ConeManagement.FailureResult;
                    }

                    succeeded = manager.TryWiden(widen);
                    break;

                case NamedPipeMessages.ConeManagement.NarrowRequest:
                    if (!NamedPipeMessages.ConeManagement.NarrowParameters.TryParse(message.Body, out NamedPipeMessages.ConeManagement.NarrowParameters narrow))
                    {
                        TraceInvalidRequest(tracer, message.Header);
                        return NamedPipeMessages.ConeManagement.FailureResult;
                    }

                    succeeded = manager.TryNarrow(narrow);
                    break;

                default:
                    throw new ArgumentException($"'{message.Header}' is not a cone request.", nameof(message));
            }

            return succeeded ? NamedPipeMessages.ConeManagement.SuccessResult : NamedPipeMessages.ConeManagement.FailureResult;
        }

        /// <summary>
        /// Add the paths of a Git command to the cone and update the index. The hook waits for
        /// the result before Git runs.
        /// </summary>
        /// <returns>True when the cone and the index include the paths.</returns>
        public bool TryWiden(NamedPipeMessages.ConeManagement.WidenParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Stopwatch stopwatch = Stopwatch.StartNew();
            EventMetadata metadata = this.CreateMetadata(parameters.CommandId);
            metadata.Add("PathCount", parameters.Paths.Count);

            ConeChange change = this.RunAndCatch(() => this.Widen(parameters, stopwatch, metadata), metadata);
            bool succeeded = change == ConeChange.Applied || change == ConeChange.ConeUnchanged;

            // A widen that takes longer than the wait of the hook is a warning: Git then runs
            // while the child Git process can still hold the index lock.
            this.TraceResult(WidenEventName, change, stopwatch.ElapsedMilliseconds, NamedPipeMessages.ConeManagement.WidenWaitBudgetMs, metadata);
            return succeeded;
        }

        /// <summary>
        /// Remove the paths of a Git command from the cone. Git has already run.
        /// </summary>
        /// <returns>True when the cone file no longer includes paths that only that command needed.</returns>
        public bool TryNarrow(NamedPipeMessages.ConeManagement.NarrowParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Stopwatch stopwatch = Stopwatch.StartNew();
            EventMetadata metadata = this.CreateMetadata(parameters.CommandId);

            ConeChange change = this.RunAndCatch(() => this.Narrow(parameters, metadata), metadata);
            bool succeeded = change == ConeChange.Narrowed ||
                change == ConeChange.ConeUnchanged ||
                change == ConeChange.NothingToNarrow ||
                change == ConeChange.NoCommandId;

            this.TraceResult(NarrowEventName, change, stopwatch.ElapsedMilliseconds, NamedPipeMessages.ConeManagement.NarrowWaitBudgetMs, metadata);
            return succeeded;
        }

        private static void TraceInvalidRequest(ITracer tracer, string header)
        {
            // Any local user can write to the mount pipe, so the body is not traced.
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(AutoSparseIndexConeManager));
            metadata.Add("Header", header);
            tracer.RelatedWarning(metadata, "Cone request has a body that is not valid", Keywords.Telemetry);
        }

        private static bool TryGetProcessId(string commandId, out int processId)
        {
            processId = 0;
            return !string.IsNullOrEmpty(commandId) &&
                int.TryParse(commandId, NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
                processId > 0;
        }

        private static bool IsFailure(ConeChange change)
        {
            switch (change)
            {
                case ConeChange.InvalidRequest:
                case ConeChange.InvalidPath:
                case ConeChange.QueueWaitTooLong:
                case ConeChange.SparseDirectoriesUnknown:
                case ConeChange.ConeFileLocked:
                case ConeChange.WriteFailed:
                case ConeChange.ApplyFailed:
                case ConeChange.UnexpectedException:
                    return true;

                default:
                    return false;
            }
        }

        private ConeChange Widen(NamedPipeMessages.ConeManagement.WidenParameters parameters, Stopwatch stopwatch, EventMetadata metadata)
        {
            if (!parameters.IsValid())
            {
                return ConeChange.InvalidRequest;
            }

            if (parameters.Paths.Count > MaxWidenPaths)
            {
                return ConeChange.TooManyPaths;
            }

            // Without a process id, nothing can remove the entries when Git ends without a narrow.
            if (!TryGetProcessId(parameters.CommandId, out int processId))
            {
                return ConeChange.NoCommandId;
            }

            // Refuse a command that does not hold the GVFS lock before it waits for other
            // requests. The check runs again under the lock, because the holder can change.
            if (!this.environment.CanWriteIndex(processId))
            {
                return ConeChange.NotLockHolder;
            }

            lock (this.coneLock)
            {
                if (!this.environment.CanWriteIndex(processId))
                {
                    return ConeChange.NotLockHolder;
                }

                if (parameters.Paths.Count <= MaxTracedPaths)
                {
                    metadata.Add("Paths", string.Join(";", parameters.Paths));
                }

                this.RemoveEndedCommands(metadata);

                if (!this.TryGetStartTime(processId, out long startTime))
                {
                    return ConeChange.CommandEnded;
                }

                if (this.transientState.IsNarrowed(processId, startTime))
                {
                    return ConeChange.CommandAlreadyNarrowed;
                }

                if (stopwatch.ElapsedMilliseconds > MaxQueueWaitBeforeApplyMs)
                {
                    return ConeChange.QueueWaitTooLong;
                }

                List<string> modifiedPaths = new List<string>(this.environment.GetModifiedPaths());

                // A modified file that exists keeps its content when Git expands its directory.
                // A deleted file, or a modified folder, does not. Only those need the list of
                // sparse-directory entries.
                List<string> unsafePaths = modifiedPaths.FindAll(
                    path => path.EndsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal) || !this.environment.IsFileInWorkingDirectory(path));
                List<string> hidden = new List<string>();
                if (unsafePaths.Count > 0)
                {
                    Stopwatch listStopwatch = Stopwatch.StartNew();
                    if (!this.environment.TryGetSparseDirectories(out IReadOnlyList<string> sparseDirectories, out string sparseDirectoriesError))
                    {
                        metadata.Add("SparseDirectoriesError", sparseDirectoriesError);
                        return ConeChange.SparseDirectoriesUnknown;
                    }

                    metadata.Add("ListSparseDirectoriesMs", listStopwatch.ElapsedMilliseconds);
                    hidden = SparseDirectoryExpansionGuard.FindHiddenModifiedPaths(unsafePaths, sparseDirectories, this.ignoreCase);
                }

                HashSet<string> hiddenSet = new HashSet<string>(hidden, StringComparer.Ordinal);
                if (hidden.Count > 0)
                {
                    metadata.Add("HiddenModifiedPathCount", hidden.Count);
                }

                List<string> conePaths = this.GetConePaths(modifiedPaths, hiddenSet);
                conePaths.AddRange(parameters.Paths);
                if (!this.TryBuildCone(conePaths, metadata, out ConePatternSet cone))
                {
                    return ConeChange.InvalidPath;
                }

                string unsafePath = SparseDirectoryExpansionGuard.FindReachedPath(cone, hidden, this.ignoreCase);
                if (unsafePath != null)
                {
                    metadata.Add("UnsafeModifiedPath", unsafePath);
                    return ConeChange.ExpandsModifiedPath;
                }

                if (!this.environment.TryLockConeFile(out IDisposable coneFileLock, out Exception lockError))
                {
                    metadata.Add("LockError", lockError?.ToString());
                    return ConeChange.ConeFileLocked;
                }

                using (coneFileLock)
                {
                    bool coneFileExisted = this.fileSystem.FileExists(this.sparseCheckoutPath);
                    ConeFileWriteResult writeResult = this.coneFileWriter.Write(this.sparseCheckoutPath, cone, out string backupPath, out Exception writeError);
                    metadata.Add("WriteResult", writeResult.ToString());
                    bool keepBackup = false;
                    try
                    {
                        if (writeResult == ConeFileWriteResult.Failed)
                        {
                            metadata.Add("WriteError", writeError?.ToString());
                            return ConeChange.WriteFailed;
                        }

                        this.hiddenModifiedPaths = hiddenSet;
                        this.coneFileMayBeStale = false;
                        if (writeResult == ConeFileWriteResult.Unchanged && !this.indexMayDifferFromCone)
                        {
                            this.transientState.Add(processId, startTime, parameters.Paths);
                            return ConeChange.ConeUnchanged;
                        }

                        // Until Git succeeds, the index can differ from the cone file.
                        this.indexMayDifferFromCone = true;
                        if (!this.TryApplyConeToIndex(metadata))
                        {
                            keepBackup = !this.TryRollBack(writeResult, coneFileExisted, backupPath, metadata);
                            this.coneFileMayBeStale = keepBackup;
                            return ConeChange.ApplyFailed;
                        }

                        this.indexMayDifferFromCone = false;
                        this.transientState.Add(processId, startTime, parameters.Paths);
                        return ConeChange.Applied;
                    }
                    finally
                    {
                        if (!keepBackup)
                        {
                            this.DeleteBackup(backupPath, metadata);
                        }
                    }
                }
            }
        }

        private ConeChange Narrow(NamedPipeMessages.ConeManagement.NarrowParameters parameters, EventMetadata metadata)
        {
            if (!TryGetProcessId(parameters.CommandId, out int processId))
            {
                return ConeChange.NoCommandId;
            }

            lock (this.coneLock)
            {
                // A narrow from a process that already ended still removes its entries. Without
                // a start time, the manager records no narrowed command: it cannot tell a later
                // process with the same id from this one.
                bool removed = this.TryGetStartTime(processId, out long startTime)
                    ? this.transientState.Remove(processId, startTime)
                    : this.transientState.Remove(processId, startTime: null);
                int ended = this.RemoveEndedCommands(metadata);
                if (!removed && ended == 0 && !this.coneFileMayBeStale)
                {
                    return ConeChange.NothingToNarrow;
                }

                // Until the cone file is written, a later request must write it again.
                this.coneFileMayBeStale = true;

                List<string> modifiedPaths = new List<string>(this.environment.GetModifiedPaths());
                if (!this.TryBuildCone(this.GetConePaths(modifiedPaths, this.hiddenModifiedPaths), metadata, out ConePatternSet cone))
                {
                    return ConeChange.InvalidPath;
                }

                if (!this.environment.TryLockConeFile(out IDisposable coneFileLock, out Exception lockError))
                {
                    metadata.Add("LockError", lockError?.ToString());
                    return ConeChange.ConeFileLocked;
                }

                using (coneFileLock)
                {
                    ConeFileWriteResult writeResult = this.coneFileWriter.Write(this.sparseCheckoutPath, cone, out string backupPath, out Exception writeError);
                    metadata.Add("WriteResult", writeResult.ToString());

                    // The narrow does not run Git, so it never restores the previous file.
                    this.DeleteBackup(backupPath, metadata);

                    switch (writeResult)
                    {
                        case ConeFileWriteResult.Failed:
                            metadata.Add("WriteError", writeError?.ToString());
                            return ConeChange.WriteFailed;

                        case ConeFileWriteResult.Unchanged:
                            this.coneFileMayBeStale = false;
                            return ConeChange.ConeUnchanged;

                        default:
                            this.coneFileMayBeStale = false;
                            this.indexMayDifferFromCone = true;
                            return ConeChange.Narrowed;
                    }
                }
            }
        }

        private ConeChange RunAndCatch(Func<ConeChange> operation, EventMetadata metadata)
        {
            try
            {
                return operation();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                metadata.Add("Exception", e.ToString());
                return ConeChange.UnexpectedException;
            }
        }

        private bool TryApplyConeToIndex(EventMetadata metadata)
        {
            Stopwatch applyStopwatch = Stopwatch.StartNew();
            try
            {
                GitProcess.Result applyResult = this.environment.ApplyConeToIndex();
                metadata.Add("ApplyMs", applyStopwatch.ElapsedMilliseconds);
                if (applyResult.ExitCodeIsFailure)
                {
                    metadata.Add("ExitCode", applyResult.ExitCode);
                    metadata.Add("Errors", applyResult.Errors);
                    return false;
                }

                return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                metadata.Add("Exception", e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Put back the cone file that was there before the widen. Return false when the
        /// previous file could not be restored.
        /// </summary>
        private bool TryRollBack(ConeFileWriteResult writeResult, bool coneFileExisted, string backupPath, EventMetadata metadata)
        {
            if (writeResult != ConeFileWriteResult.Written)
            {
                return true;
            }

            bool restored;
            Exception restoreError;
            if (backupPath != null)
            {
                restored = this.coneFileWriter.TryRestoreBackup(this.sparseCheckoutPath, backupPath, out restoreError);
            }
            else if (!coneFileExisted)
            {
                // There was no cone file before. Without one, the hooks send no requests.
                restored = this.fileSystem.TryDeleteFile(this.sparseCheckoutPath, out restoreError);
            }
            else
            {
                // The writer could not tell that a file was there and made no backup.
                restored = false;
                restoreError = null;
            }

            metadata.Add("Restored", restored);
            if (!restored)
            {
                // The cone file now includes directories that the index does not expand. Git
                // expands them in memory when it needs them. The backup stays for diagnosis.
                metadata.Add("RestoreError", restoreError?.ToString());
            }

            return restored;
        }

        private List<string> GetConePaths(List<string> modifiedPaths, HashSet<string> excludedModifiedPaths)
        {
            List<string> paths = new List<string>(modifiedPaths.Count);
            foreach (string modifiedPath in modifiedPaths)
            {
                if (!excludedModifiedPaths.Contains(modifiedPath))
                {
                    paths.Add(modifiedPath);
                }
            }

            paths.AddRange(this.transientState.GetAllPaths());
            return paths;
        }

        private bool TryBuildCone(List<string> paths, EventMetadata metadata, out ConePatternSet cone)
        {
            try
            {
                cone = ConeBuilder.BuildFromModifiedPaths(paths, this.ignoreCase);
                return true;
            }
            catch (ArgumentException e)
            {
                // A cone file cannot include this directory. Do not write a cone that leaves
                // it out, because Git then hides the path. Keep the current cone file and index.
                metadata.Add("InvalidPathException", e.ToString());
                cone = null;
                return false;
            }
        }

        private int RemoveEndedCommands(EventMetadata metadata)
        {
            int ended = this.transientState.RemoveEnded(this.IsProcessRunning);
            if (ended > 0)
            {
                metadata.Add("EndedCommandsRemoved", ended);
            }

            return ended;
        }

        /// <summary>
        /// Read the start time of a Git process. Return false when the start time is not
        /// available: the process ended, or the mount cannot open it.
        /// </summary>
        private bool TryGetStartTime(int processId, out long startTime)
        {
            return this.environment.TryGetProcessStartTime(processId, out startTime) == ProcessStartTimeResult.Success;
        }

        private bool IsProcessRunning(int processId, long? startTime)
        {
            switch (this.environment.TryGetProcessStartTime(processId, out long currentStartTime))
            {
                case ProcessStartTimeResult.Success:
                    return startTime == null || startTime == currentStartTime;

                case ProcessStartTimeResult.Indeterminate:
                    // Not known. Keep the entries: a cone that is too large is safe.
                    return true;

                default:
                    // ProcessNotFound: the process ended. Inaccessible: the mount opened this
                    // process before, so another process now has this id.
                    return false;
            }
        }

        private void DeleteBackup(string backupPath, EventMetadata metadata)
        {
            if (backupPath != null && !this.fileSystem.TryDeleteFile(backupPath, out Exception deleteError))
            {
                metadata.Add("DeleteBackupError", deleteError?.ToString());
            }
        }

        private EventMetadata CreateMetadata(string commandId)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(AutoSparseIndexConeManager));
            metadata.Add("CommandId", commandId);
            return metadata;
        }

        private void TraceResult(string eventName, ConeChange change, long durationMs, int waitBudgetMs, EventMetadata metadata)
        {
            metadata.Add("Result", change.ToString());
            metadata.Add("DurationMs", durationMs);
            metadata.Add("TransientCommandCount", this.GetTransientCommandCount());

            bool exceededWaitBudget = durationMs > waitBudgetMs;
            if (exceededWaitBudget)
            {
                metadata.Add("WaitBudgetMs", waitBudgetMs);
            }

            if (IsFailure(change) || exceededWaitBudget || metadata.ContainsKey("DeleteBackupError"))
            {
                this.tracer.RelatedEvent(EventLevel.Warning, eventName, metadata, Keywords.Telemetry);
            }
            else
            {
                this.tracer.RelatedEvent(EventLevel.Informational, eventName, metadata);
            }
        }

        private int GetTransientCommandCount()
        {
            lock (this.coneLock)
            {
                return this.transientState.CommandCount;
            }
        }
    }
}
