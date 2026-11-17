using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Applies the cone widen and narrow requests that the Git hooks send to the mount when
    /// <c>gvfs.auto-sparse-index</c> is true.
    /// </summary>
    /// <remarks>
    /// The cone is computed from the modified paths that can differ from HEAD, plus the changed
    /// paths that the modified paths do not have yet, plus the transient paths of every Git
    /// command that widened and did not narrow yet (see <see cref="TransientConeState"/>).
    /// When the last command narrows, the cone is again the cone of the modified paths.
    /// <para>
    /// Widen: write the new cone file, then run <see cref="IConeManagerEnvironment.ApplyConeToIndex"/>
    /// so that the index has entries for the new directories before Git runs. If Git fails,
    /// restore the previous cone file. Only the Git command that holds the GVFS lock can
    /// widen: it waits in its pre-command hook, so no other Git command writes the index.
    /// Unless Git changes only the index when the cone changes
    /// (<see cref="IConeManagerEnvironment.SparsityChangeIsIndexOnly"/>), the cone does not
    /// include the directory of a deleted file that a sparse-directory entry hides (see
    /// <see cref="SparseDirectoryExpansionGuard"/>).
    /// </para>
    /// <para>
    /// Narrow: write the new cone file only, after the post-command hook of a command that
    /// widened, or after a background check (see below). The mount cannot replace the index then
    /// (see <see cref="IConeManagerEnvironment.CanWriteIndex"/>). The index keeps the expanded
    /// entries until the next widen collapses each directory outside the cone whose entries
    /// are all skip-worktree.
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
    /// <para>
    /// Working-tree changes: Git sees a change to a file only when the index has an entry for
    /// it, and <c>git add</c> takes a path only when the cone includes it. The file system
    /// callbacks report every created, written, renamed, or deleted path (see
    /// <see cref="OnWorkingDirectoryPathChanged"/>). When the applied cone does not include the
    /// path, the manager creates the pending file (see
    /// <see cref="IConeManagerEnvironment.TrySetConeChangePending"/>). The callback does not
    /// wait for a Git command. The pre-command hook of the next Git command that reads the
    /// index sees the file and sends an apply request while that command holds the GVFS lock.
    /// The apply widens the cone like a widen without request paths. If the apply fails, the
    /// change stays pending for the next Git command.
    /// </para>
    /// <para>
    /// Clean paths: the cone includes a modified path only while the path can differ from HEAD.
    /// After a Git command releases the GVFS lock, and when changes inside the applied cone stop
    /// for <see cref="DefaultChangeCheckDelayMs"/>, a background check (see
    /// <see cref="TryReconcile"/>) asks Git which paths differ from HEAD. A modified
    /// file that matches HEAD, the index, and the working tree is clean, and the manager writes
    /// a cone file without it. Git cannot collapse its directory while the file is a modified
    /// path, because the virtual file system hook clears its skip-worktree bit, so the index
    /// keeps its entry. The next change to the file makes it a candidate again.
    /// </para>
    /// </remarks>
    public class AutoSparseIndexConeManager : IWorkingDirectoryChangeObserver
    {
        /// <summary>A widen with more paths is refused, and Git expands the index in memory.</summary>
        public const int MaxWidenPaths = 1000;

        /// <summary>
        /// After a change inside the applied cone, the manager waits until no change came for
        /// this long, and then checks which modified files match HEAD again.
        /// </summary>
        public const int DefaultChangeCheckDelayMs = 2000;

        private const string WidenEventName = "ConeWiden";
        private const string NarrowEventName = "ConeNarrow";
        private const string ApplyEventName = "ConeApply";
        private const string ReconcileEventName = "ConeReconcile";
        private const int MaxTracedPaths = 10;

        // A widen that waited longer than this for earlier requests does not start Git: the hook
        // stops its wait at WidenWaitBudgetMs, and Git then needs the index lock that the child
        // Git process holds.
        private const int MaxQueueWaitBeforeApplyMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs / 2;

        private static readonly IReadOnlyList<string> NoPaths = new List<string>();

        private readonly ITracer tracer;
        private readonly PhysicalFileSystem fileSystem;
        private readonly ConeFileWriter coneFileWriter;
        private readonly string sparseCheckoutPath;
        private readonly bool ignoreCase;
        private readonly IConeManagerEnvironment environment;
        private readonly TransientConeState transientState = new TransientConeState();
        private readonly Lock coneLock = new Lock();
        private readonly Lock pendingLock = new Lock();
        private readonly Lock reconcileScheduleLock = new Lock();
        private readonly StringComparer pathComparer;
        private readonly int changeCheckDelayMs;
        private readonly Timer changeCheckTimer;

        // The working-tree paths that changed since an apply or a check last used them.
        private readonly ConcurrentDictionary<string, PathChange> changedPaths;

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

        // The modified files that the last check found equal to HEAD, the index, and the working
        // tree. The cone leaves them out. Guarded by coneLock.
        private HashSet<string> cleanPaths;

        // Changed paths that an apply used before the modified paths had them (see
        // ConsumeChanges). The cone includes them. Guarded by coneLock.
        private HashSet<string> unconfirmedPaths;

        // The directories that the cone file includes and the index expands, or null when that
        // is not known.
        private AppliedConeSnapshot appliedCone;

        private long changeSequence;
        private bool isPending;

        // Changes when an apply or a widen uses the changed paths, so that a check that started
        // before it does not use its result. Guarded by coneLock.
        private long consumeEpoch;

        // The state that the last check saw. A check with the same state does not run Git again.
        private (long ChangeSequence, long ConsumeEpoch, long RepositoryStamp)? lastReconcileState;

        private bool reconcileRunning;
        private bool reconcileRequested;
        private volatile bool isStopped;

        public AutoSparseIndexConeManager(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string sparseCheckoutPath,
            bool ignoreCase,
            IConeManagerEnvironment environment,
            int changeCheckDelayMs = DefaultChangeCheckDelayMs)
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
            this.pathComparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            this.changedPaths = new ConcurrentDictionary<string, PathChange>(this.pathComparer);
            this.cleanPaths = new HashSet<string>(this.pathComparer);
            this.unconfirmedPaths = new HashSet<string>(this.pathComparer);
            this.changeCheckDelayMs = changeCheckDelayMs;
            this.changeCheckTimer = new Timer(_ => this.ScheduleReconcile(), state: null, Timeout.Infinite, Timeout.Infinite);
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
            ConeChangePending,
            NothingChanged,
            StateChanged,
            CleanPathChanged,
            DiffFailed,
            Stopped,
        }

        /// <summary>
        /// Handle a <see cref="NamedPipeMessages.ConeManagement.WidenRequest"/>,
        /// <see cref="NamedPipeMessages.ConeManagement.NarrowRequest"/>, or
        /// <see cref="NamedPipeMessages.ConeManagement.ApplyRequest"/> and return the reply.
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

                case NamedPipeMessages.ConeManagement.ApplyRequest:
                    if (!NamedPipeMessages.ConeManagement.ApplyParameters.TryParse(message.Body, out NamedPipeMessages.ConeManagement.ApplyParameters apply))
                    {
                        TraceInvalidRequest(tracer, message.Header);
                        return NamedPipeMessages.ConeManagement.FailureResult;
                    }

                    succeeded = manager.TryApply(apply);
                    break;

                default:
                    throw new ArgumentException($"'{message.Header}' is not a cone request.", nameof(message));
            }

            return succeeded ? NamedPipeMessages.ConeManagement.SuccessResult : NamedPipeMessages.ConeManagement.FailureResult;
        }

        /// <summary>
        /// Add the paths of a Git command to the cone and update the index. The hook waits for
        /// the result before Git runs. The cone also gets every pending working-tree change.
        /// </summary>
        /// <returns>True when the cone and the index include the paths.</returns>
        public bool TryWiden(NamedPipeMessages.ConeManagement.WidenParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Stopwatch stopwatch = Stopwatch.StartNew();
            EventMetadata metadata = this.CreateMetadata(parameters.CommandId);
            metadata.Add("PathCount", parameters.Paths.Count);

            ApplyAttempt attempt = new ApplyAttempt();
            ConeChange change = this.RunAndCatch(() => this.Widen(parameters, attempt, stopwatch, metadata), metadata);
            this.RearmPendingAfterAttempt(attempt, change, requestHasPaths: true, metadata);
            bool succeeded = change == ConeChange.Applied || change == ConeChange.ConeUnchanged;

            // A widen that takes longer than the wait of the hook is a warning: Git then runs
            // while the child Git process can still hold the index lock.
            this.TraceResult(WidenEventName, change, stopwatch.ElapsedMilliseconds, NamedPipeMessages.ConeManagement.WidenWaitBudgetMs, metadata);
            return succeeded;
        }

        /// <summary>
        /// Apply the pending working-tree changes to the cone and the index before a Git command
        /// reads the index. The hook waits for the result before Git runs.
        /// </summary>
        /// <returns>True when the cone and the index include every pending change.</returns>
        public bool TryApply(NamedPipeMessages.ConeManagement.ApplyParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Stopwatch stopwatch = Stopwatch.StartNew();
            EventMetadata metadata = this.CreateMetadata(parameters.CommandId);

            ApplyAttempt attempt = new ApplyAttempt();
            ConeChange change = this.RunAndCatch(
                () => TryGetProcessId(parameters.CommandId, out int processId)
                    ? this.ApplyCone(processId, NoPaths, NamedPipeMessages.ConeManagement.ApplyWaitBudgetMs / 2, attempt, stopwatch, metadata)
                    : ConeChange.NoCommandId,
                metadata);
            this.RearmPendingAfterAttempt(attempt, change, requestHasPaths: false, metadata);
            bool succeeded = change == ConeChange.Applied || change == ConeChange.ConeUnchanged;

            this.TraceResult(ApplyEventName, change, stopwatch.ElapsedMilliseconds, NamedPipeMessages.ConeManagement.ApplyWaitBudgetMs, metadata);
            return succeeded;
        }

        /// <summary>
        /// Start to track working-tree changes. Changes made while no mount ran do not reach the
        /// manager, so the first Git command applies the cone of every modified path.
        /// </summary>
        public void Start()
        {
            this.SetPending();
        }

        /// <summary>Stop the background checks. Requests and callbacks after this do nothing.</summary>
        public void Stop()
        {
            this.isStopped = true;
            this.changeCheckTimer.Dispose();
        }

        /// <inheritdoc/>
        public void OnWorkingDirectoryPathChanged(string relativePath, bool isFolder)
        {
            if (this.isStopped || string.IsNullOrEmpty(relativePath))
            {
                return;
            }

            try
            {
                string path = NormalizeModifiedPath(relativePath, isFolder);
                if (path.Length == 0 || path == GVFSConstants.GitPathSeparatorString)
                {
                    return;
                }

                // Record the change before reading the cone. A cone change then either sees this
                // record, or this method sees the new cone (see PublishCone).
                long sequence = Interlocked.Increment(ref this.changeSequence);
                PathChange uncovered = new PathChange(sequence, WasCovered: false);
                this.changedPaths[path] = uncovered;

                AppliedConeSnapshot cone = Volatile.Read(ref this.appliedCone);
                if (cone != null && cone.IsCovered(path))
                {
                    this.changedPaths.TryUpdate(path, new PathChange(sequence, WasCovered: true), uncovered);

                    // The file can have the content of HEAD again. Check when the changes stop.
                    this.changeCheckTimer.Change(this.changeCheckDelayMs, Timeout.Infinite);
                }
                else
                {
                    this.SetPending();
                }
            }
            catch (ObjectDisposedException) when (this.isStopped)
            {
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // The cone can miss this change. The next Git command that names the path, or the
                // next mount, adds it.
                EventMetadata metadata = this.CreateMetadata(commandId: null);
                metadata.Add("Exception", e.ToString());
                this.tracer.RelatedWarning(metadata, nameof(this.OnWorkingDirectoryPathChanged) + " failed", Keywords.Telemetry);
            }
        }

        /// <summary>
        /// A Git command released the GVFS lock. Start a background check for modified files
        /// that match HEAD again (see <see cref="TryReconcile"/>). Returns at once.
        /// </summary>
        public void OnGitCommandCompleted()
        {
            this.ScheduleReconcile();
        }

        /// <summary>
        /// Ask Git which paths differ from HEAD, and write a cone file without the modified files
        /// that match HEAD. A clean file that differs again goes back into the cone. Git does not
        /// write the index, and the manager does not wait for the GVFS lock.
        /// </summary>
        /// <returns>True when the check ran or was not needed.</returns>
        public bool TryReconcile()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            EventMetadata metadata = this.CreateMetadata(commandId: null);
            ConeChange change = this.RunAndCatch(() => this.Reconcile(metadata), metadata);

            bool succeeded;
            switch (change)
            {
                case ConeChange.NothingChanged:
                case ConeChange.ConeChangePending:
                case ConeChange.StateChanged:
                case ConeChange.Stopped:
                    // Nothing to report: the check did not change anything.
                    return true;

                case ConeChange.Narrowed:
                case ConeChange.ConeUnchanged:
                case ConeChange.CleanPathChanged:
                    succeeded = true;
                    break;

                default:
                    succeeded = false;
                    break;
            }

            this.TraceResult(ReconcileEventName, change, stopwatch.ElapsedMilliseconds, waitBudgetMs: int.MaxValue, metadata);
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
                case ConeChange.DiffFailed:
                    return true;

                default:
                    return false;
            }
        }

        private static string NormalizeModifiedPath(string path, bool isFolder)
        {
            // The same form as the modified-paths database: forward slashes, no leading or
            // trailing slash, and a trailing slash for a folder.
            string normalized = path.Replace('\\', GVFSConstants.GitPathSeparator).Trim(GVFSConstants.GitPathSeparator);
            return isFolder ? normalized + GVFSConstants.GitPathSeparatorString : normalized;
        }

        private static bool IsFolderPath(string modifiedPath)
        {
            return modifiedPath.EndsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal);
        }

        private ConeChange Widen(NamedPipeMessages.ConeManagement.WidenParameters parameters, ApplyAttempt attempt, Stopwatch stopwatch, EventMetadata metadata)
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

            if (parameters.Paths.Count <= MaxTracedPaths)
            {
                metadata.Add("Paths", string.Join(";", parameters.Paths));
            }

            return this.ApplyCone(processId, parameters.Paths, MaxQueueWaitBeforeApplyMs, attempt, stopwatch, metadata);
        }

        /// <summary>
        /// Write the cone of the modified paths, the transient paths, and
        /// <paramref name="requestPaths"/>, and apply it to the index for the Git process that
        /// holds the GVFS lock.
        /// </summary>
        private ConeChange ApplyCone(int processId, IReadOnlyList<string> requestPaths, int maxQueueWaitMs, ApplyAttempt attempt, Stopwatch stopwatch, EventMetadata metadata)
        {
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

                this.RemoveEndedCommands(metadata);

                if (!this.TryGetStartTime(processId, out long startTime))
                {
                    return ConeChange.CommandEnded;
                }

                if (requestPaths.Count > 0 && this.transientState.IsNarrowed(processId, startTime))
                {
                    return ConeChange.CommandAlreadyNarrowed;
                }

                if (stopwatch.ElapsedMilliseconds > maxQueueWaitMs)
                {
                    return ConeChange.QueueWaitTooLong;
                }

                // From here, this request applies every change that is pending now. A change
                // after this point makes the pending file again (see OnWorkingDirectoryPathChanged).
                attempt.ClearedPending = this.ClearPending();
                long consumedSequence = Interlocked.Read(ref this.changeSequence);

                List<string> modifiedPaths = new List<string>(this.environment.GetModifiedPaths());
                this.ConsumeChanges(consumedSequence, modifiedPaths, metadata);

                // A modified file that exists keeps its content when Git expands its directory.
                // A deleted file, or a modified folder, does not, unless Git changes only the
                // index. Only those need the list of sparse-directory entries, and only when the
                // applied cone does not already expand them. A clean path has no index entry that
                // differs from the working tree.
                AppliedConeSnapshot applied = Volatile.Read(ref this.appliedCone);
                List<string> unsafePaths = new List<string>();
                if (!this.environment.SparsityChangeIsIndexOnly)
                {
                    foreach (string path in modifiedPaths.Concat(this.unconfirmedPaths))
                    {
                        if ((IsFolderPath(path) || !this.environment.IsFileInWorkingDirectory(path)) &&
                            !this.cleanPaths.Contains(path) &&
                            (applied == null || !applied.IsCovered(path)))
                        {
                            unsafePaths.Add(path);
                        }
                    }
                }

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
                conePaths.AddRange(requestPaths);
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
                            this.AddTransientPaths(processId, startTime, requestPaths);
                            this.PublishCone(cone);
                            return ConeChange.ConeUnchanged;
                        }

                        // Until Git succeeds, the index can differ from the cone file.
                        this.indexMayDifferFromCone = true;
                        if (!this.TryApplyConeToIndex(metadata))
                        {
                            keepBackup = !this.TryRollBack(writeResult, coneFileExisted, backupPath, metadata);
                            this.coneFileMayBeStale = keepBackup;
                            if (keepBackup)
                            {
                                // The cone file now has directories that the index did not expand.
                                this.PublishCone(null);
                            }

                            return ConeChange.ApplyFailed;
                        }

                        this.indexMayDifferFromCone = false;
                        this.AddTransientPaths(processId, startTime, requestPaths);
                        this.PublishCone(cone);
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

                ConeChange change = this.WriteNarrowerCone(metadata);
                if (change == ConeChange.Narrowed)
                {
                    // The index keeps the directories of the transient paths until the next widen.
                    this.indexMayDifferFromCone = true;
                }

                return change;
            }
        }

        /// <summary>
        /// Write the cone of the modified paths and the transient paths without running Git.
        /// The caller holds <see cref="coneLock"/>, and the new cone does not include a directory
        /// that the index does not expand.
        /// </summary>
        private ConeChange WriteNarrowerCone(EventMetadata metadata)
        {
            // Until the cone file is written, a later request must write it again.
            this.coneFileMayBeStale = true;

            List<string> modifiedPaths = new List<string>(this.environment.GetModifiedPaths());
            List<string> conePaths = this.GetConePaths(modifiedPaths, this.hiddenModifiedPaths);
            if (!this.TryBuildCone(conePaths, metadata, out ConePatternSet cone))
            {
                return ConeChange.InvalidPath;
            }

            // A modified path that is newer than the last apply can be in a directory that the
            // index does not expand. The cone file then has it, so the next apply must run Git
            // even when the cone file does not change.
            AppliedConeSnapshot applied = Volatile.Read(ref this.appliedCone);
            bool hasUnappliedPaths = applied == null || conePaths.Exists(path => !applied.IsCovered(path));

            if (!this.environment.TryLockConeFile(out IDisposable coneFileLock, out Exception lockError))
            {
                metadata.Add("LockError", lockError?.ToString());
                return ConeChange.ConeFileLocked;
            }

            using (coneFileLock)
            {
                ConeFileWriteResult writeResult = this.coneFileWriter.Write(this.sparseCheckoutPath, cone, out string backupPath, out Exception writeError);
                metadata.Add("WriteResult", writeResult.ToString());

                // A narrow does not run Git, so it never restores the previous file.
                this.DeleteBackup(backupPath, metadata);

                switch (writeResult)
                {
                    case ConeFileWriteResult.Failed:
                        metadata.Add("WriteError", writeError?.ToString());
                        return ConeChange.WriteFailed;

                    case ConeFileWriteResult.Unchanged:
                        this.coneFileMayBeStale = false;
                        this.PublishConeIfKnown(cone);
                        return ConeChange.ConeUnchanged;

                    default:
                        this.coneFileMayBeStale = false;
                        if (hasUnappliedPaths)
                        {
                            metadata.Add("HasUnappliedPaths", true);
                            this.indexMayDifferFromCone = true;
                        }

                        this.PublishConeIfKnown(cone);
                        return ConeChange.Narrowed;
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
                if (!excludedModifiedPaths.Contains(modifiedPath) && !this.cleanPaths.Contains(modifiedPath))
                {
                    paths.Add(modifiedPath);
                }
            }

            foreach (string unconfirmedPath in this.unconfirmedPaths)
            {
                if (!excludedModifiedPaths.Contains(unconfirmedPath))
                {
                    paths.Add(unconfirmedPath);
                }
            }

            paths.AddRange(this.transientState.GetAllPaths());
            return paths;
        }

        private void AddTransientPaths(int processId, long startTime, IReadOnlyList<string> requestPaths)
        {
            if (requestPaths.Count > 0)
            {
                this.transientState.Add(processId, startTime, requestPaths);
            }
        }

        /// <summary>
        /// Use the working-tree changes up to <paramref name="sequence"/>: a clean file that
        /// changed can differ from HEAD again. The caller holds <see cref="coneLock"/>.
        /// </summary>
        /// <remarks>
        /// The mount adds a changed path to the modified paths in a background task, and that
        /// task waits while a Git command holds the GVFS lock. So a change can arrive before
        /// its modified path. Such a path stays in the cone as an unconfirmed path until the
        /// modified paths have it, or until a check finds it clean.
        /// </remarks>
        private void ConsumeChanges(long sequence, List<string> modifiedPaths, EventMetadata metadata)
        {
            HashSet<string> modified = new HashSet<string>(modifiedPaths, this.pathComparer);
            int consumed = 0;
            foreach (KeyValuePair<string, PathChange> change in this.changedPaths)
            {
                if (change.Value.Sequence <= sequence &&
                    ((ICollection<KeyValuePair<string, PathChange>>)this.changedPaths).Remove(change))
                {
                    this.cleanPaths.Remove(change.Key);
                    if (!IsModifiedPathOrUnderModifiedFolder(change.Key, modified))
                    {
                        this.unconfirmedPaths.Add(change.Key);
                    }

                    consumed++;
                }
            }

            this.RemoveConfirmedAndStalePaths(modified);
            this.consumeEpoch++;
            metadata.Add("ConsumedChangeCount", consumed);
            metadata.Add("CleanPathCount", this.cleanPaths.Count);
            if (this.unconfirmedPaths.Count > 0)
            {
                metadata.Add("UnconfirmedPathCount", this.unconfirmedPaths.Count);
            }
        }

        private static bool IsModifiedPathOrUnderModifiedFolder(string path, HashSet<string> modified)
        {
            if (modified.Contains(path))
            {
                return true;
            }

            // The modified paths do not list a file or folder inside a modified folder.
            string trimmed = path.TrimEnd(GVFSConstants.GitPathSeparator);
            for (int separator = trimmed.LastIndexOf(GVFSConstants.GitPathSeparator); separator > 0; separator = trimmed.LastIndexOf(GVFSConstants.GitPathSeparator, separator - 1))
            {
                if (modified.Contains(trimmed.Substring(0, separator + 1)))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveConfirmedAndStalePaths(HashSet<string> modified)
        {
            // A dehydrated file leaves the modified paths. It is not clean or dirty any more.
            this.cleanPaths.RemoveWhere(path => !modified.Contains(path));
            this.unconfirmedPaths.RemoveWhere(path => IsModifiedPathOrUnderModifiedFolder(path, modified));
        }

        /// <summary>
        /// Record that the index has entries for every directory of <paramref name="cone"/>, and
        /// that the cone file is <paramref name="cone"/>. Null means that this is not known.
        /// </summary>
        private void PublishCone(ConePatternSet cone)
        {
            this.PublishSnapshot(cone == null ? null : new AppliedConeSnapshot(new ConeCoverage(cone)));
        }

        /// <summary>
        /// Record a cone file that the manager wrote without running Git. The index can lack
        /// entries for a directory that the new cone adds, so only the directories that both
        /// cones include count as applied.
        /// </summary>
        private void PublishConeIfKnown(ConePatternSet cone)
        {
            AppliedConeSnapshot current = Volatile.Read(ref this.appliedCone);
            this.PublishSnapshot(current?.Intersect(new ConeCoverage(cone)));
        }

        private void PublishSnapshot(AppliedConeSnapshot snapshot)
        {
            Volatile.Write(ref this.appliedCone, snapshot);
            Interlocked.MemoryBarrier();

            // A change that read the previous cone may need the new one. See
            // OnWorkingDirectoryPathChanged for the order of the two steps.
            foreach (KeyValuePair<string, PathChange> change in this.changedPaths)
            {
                if (snapshot == null || !snapshot.IsCovered(change.Key))
                {
                    this.SetPending();
                    return;
                }
            }
        }

        private void SetPending()
        {
            if (Volatile.Read(ref this.isPending))
            {
                return;
            }

            lock (this.pendingLock)
            {
                if (this.isPending)
                {
                    return;
                }

                if (this.environment.TrySetConeChangePending(true, out Exception error))
                {
                    this.isPending = true;
                    return;
                }

                // Without the file, the hook does not ask for the change. The next change, or
                // the next Git command that names the path, tries again.
                EventMetadata metadata = this.CreateMetadata(commandId: null);
                metadata.Add("Exception", error?.ToString());
                this.tracer.RelatedWarning(metadata, "Could not create the cone change pending file", Keywords.Telemetry);
            }
        }

        /// <summary>Delete the pending file. Return true when a change was pending.</summary>
        private bool ClearPending()
        {
            lock (this.pendingLock)
            {
                if (!this.isPending)
                {
                    return false;
                }

                if (!this.environment.TrySetConeChangePending(false, out Exception error))
                {
                    // The file stays, and so does the pending state: the next request deletes
                    // the file again. Until then, each Git command sends a request.
                    EventMetadata metadata = this.CreateMetadata(commandId: null);
                    metadata.Add("Exception", error?.ToString());
                    this.tracer.RelatedWarning(metadata, "Could not delete the cone change pending file", Keywords.Telemetry);
                    return true;
                }

                Volatile.Write(ref this.isPending, false);
                return true;
            }
        }

        private void RearmPendingAfterAttempt(ApplyAttempt attempt, ConeChange change, bool requestHasPaths, EventMetadata metadata)
        {
            if (!attempt.ClearedPending)
            {
                return;
            }

            bool rearm;
            switch (change)
            {
                case ConeChange.Applied:
                case ConeChange.ConeUnchanged:
                    return;

                case ConeChange.ExpandsModifiedPath:
                case ConeChange.InvalidPath:
                    // The pending changes alone cannot make the same cone valid, so a retry
                    // fails again until the working tree changes. The request paths can be the
                    // cause, and an apply without them can succeed.
                    rearm = requestHasPaths;
                    break;

                default:
                    // Without the apply, Git does not see the change, and Git has no fallback
                    // for that. Every later Git command tries again.
                    rearm = true;
                    break;
            }

            metadata.Add("PendingKept", rearm);
            if (rearm)
            {
                this.SetPending();
            }
        }

        /// <summary>Run <see cref="TryReconcile"/> in the background, at most one at a time.</summary>
        private void ScheduleReconcile()
        {
            if (this.isStopped)
            {
                return;
            }

            lock (this.reconcileScheduleLock)
            {
                if (this.reconcileRunning)
                {
                    this.reconcileRequested = true;
                    return;
                }

                this.reconcileRunning = true;
            }

            // The check runs Git and can take seconds, so it gets its own thread.
            Task.Factory.StartNew(
                () =>
                {
                    while (true)
                    {
                        this.TryReconcile();
                        lock (this.reconcileScheduleLock)
                        {
                            if (!this.reconcileRequested || this.isStopped)
                            {
                                this.reconcileRunning = false;
                                return;
                            }

                            this.reconcileRequested = false;
                        }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private ConeChange Reconcile(EventMetadata metadata)
        {
            if (this.isStopped)
            {
                return ConeChange.Stopped;
            }

            long startSequence;
            long epoch;
            AppliedConeSnapshot cone;
            List<string> candidates;
            (long, long, long) state;
            lock (this.coneLock)
            {
                // A pending change can be in a directory that the index does not expand, so Git
                // does not report it. Check after the next apply.
                cone = Volatile.Read(ref this.appliedCone);
                if (Volatile.Read(ref this.isPending) || cone == null)
                {
                    return ConeChange.ConeChangePending;
                }

                startSequence = Interlocked.Read(ref this.changeSequence);
                epoch = this.consumeEpoch;
                state = (startSequence, epoch, this.environment.GetRepositoryStamp());
                if (this.lastReconcileState == state)
                {
                    return ConeChange.NothingChanged;
                }

                // Only a file that the index has an entry for can be clean: one in an applied
                // directory, or one that an earlier check found clean while its directory was
                // applied. Folders stay in the cone.
                candidates = new List<string>();
                HashSet<string> seen = new HashSet<string>(this.pathComparer);
                foreach (string path in this.environment.GetModifiedPaths().Concat(this.unconfirmedPaths))
                {
                    if (!IsFolderPath(path) &&
                        !this.hiddenModifiedPaths.Contains(path) &&
                        (this.cleanPaths.Contains(path) || cone.IsCovered(path)) &&
                        seen.Add(path))
                    {
                        candidates.Add(path);
                    }
                }

                if (candidates.Count == 0)
                {
                    this.lastReconcileState = state;
                    return ConeChange.NothingChanged;
                }
            }

            metadata.Add("CandidateCount", candidates.Count);
            Stopwatch diffStopwatch = Stopwatch.StartNew();
            if (!this.environment.TryGetPathsThatDifferFromHead(out ICollection<string> differingPaths, out string diffError))
            {
                metadata.Add("DiffError", diffError);
                return ConeChange.DiffFailed;
            }

            metadata.Add("DiffMs", diffStopwatch.ElapsedMilliseconds);
            if (this.isStopped)
            {
                return ConeChange.Stopped;
            }

            HashSet<string> differing = new HashSet<string>(differingPaths, this.pathComparer);

            // A file is clean when Git reports no difference, and the working tree has it
            // exactly when the index has it. An untracked file, and a deleted tracked file,
            // differ from HEAD.
            Dictionary<string, bool> isClean = new Dictionary<string, bool>(candidates.Count, this.pathComparer);
            foreach (string path in candidates)
            {
                isClean[path] = !differing.Contains(path) &&
                    this.environment.IsFileInWorkingDirectory(path) == this.environment.IsTracked(path);
            }

            lock (this.coneLock)
            {
                if (this.consumeEpoch != epoch || this.isStopped)
                {
                    // An apply changed the clean paths or the index while Git ran.
                    return ConeChange.StateChanged;
                }

                int newlyClean = 0;
                int newlyDirty = 0;
                foreach (KeyValuePair<string, bool> result in isClean)
                {
                    bool changedDuringCheck = this.changedPaths.TryGetValue(result.Key, out PathChange change) &&
                        (change.Sequence > startSequence || !change.WasCovered);
                    if (result.Value && !changedDuringCheck)
                    {
                        // A clean unconfirmed path is not a modified path (for example, a file
                        // that Git wrote), so it leaves the cone without becoming a clean path.
                        if (this.unconfirmedPaths.Remove(result.Key) || this.cleanPaths.Add(result.Key))
                        {
                            newlyClean++;
                        }
                    }
                    else if (this.cleanPaths.Remove(result.Key))
                    {
                        newlyDirty++;
                    }
                }

                // Git saw each change that came before the check in a directory that the index
                // expands, so the result above already includes it.
                foreach (KeyValuePair<string, PathChange> change in this.changedPaths)
                {
                    if (change.Value.Sequence <= startSequence && change.Value.WasCovered)
                    {
                        ((ICollection<KeyValuePair<string, PathChange>>)this.changedPaths).Remove(change);
                    }
                }

                this.lastReconcileState = state;
                metadata.Add("NewlyCleanCount", newlyClean);
                metadata.Add("NewlyDirtyCount", newlyDirty);
                metadata.Add("CleanPathCount", this.cleanPaths.Count);

                if (newlyDirty > 0)
                {
                    // The cone must include the file again. Its directory still has index
                    // entries, but only an apply writes a wider cone file.
                    this.SetPending();
                    return ConeChange.CleanPathChanged;
                }

                if (newlyClean == 0)
                {
                    return ConeChange.ConeUnchanged;
                }

                return this.WriteNarrowerCone(metadata);
            }
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

            // A hidden modified path stays invisible to Git, so it is a warning too.
            if (IsFailure(change) || exceededWaitBudget || metadata.ContainsKey("DeleteBackupError") || metadata.ContainsKey("HiddenModifiedPathCount"))
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

        /// <summary>A working-tree change: its order, and if the applied cone included the path then.</summary>
        private readonly record struct PathChange(long Sequence, bool WasCovered);

        /// <summary>Records whether a request cleared the pending file.</summary>
        private sealed class ApplyAttempt
        {
            public bool ClearedPending { get; set; }
        }

        /// <summary>
        /// The directories that the cone file includes and the index expands: those that every
        /// cone in the list includes.
        /// </summary>
        private sealed class AppliedConeSnapshot
        {
            // More cones make every callback slower. Then the manager treats the state as
            // unknown, and the next Git command applies the cone again.
            private const int MaxCones = 4;

            private readonly ConeCoverage[] cones;

            public AppliedConeSnapshot(params ConeCoverage[] cones)
            {
                this.cones = cones;
            }

            public bool IsCovered(string path)
            {
                foreach (ConeCoverage cone in this.cones)
                {
                    if (!cone.IsCovered(path))
                    {
                        return false;
                    }
                }

                return true;
            }

            public AppliedConeSnapshot Intersect(ConeCoverage cone)
            {
                if (this.cones.Length >= MaxCones)
                {
                    return null;
                }

                ConeCoverage[] combined = new ConeCoverage[this.cones.Length + 1];
                this.cones.CopyTo(combined, 0);
                combined[this.cones.Length] = cone;
                return new AppliedConeSnapshot(combined);
            }
        }
    }
}
