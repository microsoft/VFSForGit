using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GVFS.Hooks
{
    /// <summary>
    /// Sends automatic sparse-index cone requests from the pre-command and post-command hooks.
    /// </summary>
    /// <remarks>
    /// When <c>gvfs.auto-sparse-index</c> is true and a Git command names paths outside the
    /// sparse-index cone, the pre-command hook asks the mount to widen the cone before Git
    /// runs. Otherwise Git expands the whole sparse index in memory to find those paths.
    /// The post-command hook asks the mount to narrow the cone again.
    /// <para>
    /// This class never fails a Git command itself. Every failure, including no mount, a mount
    /// that does not know the request, a slow mount, and an exception, gives an outcome, and
    /// <see cref="PreCommandConeRequests"/> decides with it: a command that writes the index
    /// stops before Git runs, and other commands run (Git is then slower, but correct).
    /// The hook waits at most
    /// <see cref="NamedPipeMessages.ConeManagement.WidenWaitBudgetMs"/> to connect and send
    /// a widen. After it sent a widen, it waits for the reply, at most
    /// <see cref="NamedPipeMessages.ConeManagement.WidenReplyCeilingMs"/>: the mount applies
    /// the widen with a Git process that holds the index lock, and the user's Git must not
    /// start before that process ends.
    /// </para>
    /// <para>
    /// The hook knows only the paths on the command line. It does not read a
    /// <c>--pathspec-from-file</c> file. A rebase or merge can stop on a conflict in a file
    /// outside the cone, and those paths come from the commits. For these commands Git
    /// expands the index, and the hook widens the cone when the user later names the path
    /// (for example with <c>git add</c>).
    /// </para>
    /// <para>
    /// All dependencies are delegates, so that unit tests can run every path without a mount,
    /// a repository, or a working tree.
    /// </para>
    /// </remarks>
    internal sealed class ConeManagementHook
    {
        private const string StashCommand = "stash";

        private readonly ReadSparseIndexConfig readConfig;
        private readonly Func<string> readConeFile;
        private readonly Func<NamedPipeClient> createPipeClient;
        private readonly string hookCurrentDirectory;
        private readonly Func<string> getWorkingDirectoryRoot;
        private readonly string gitPrefix;
        private readonly Action<string> trace;

        /// <param name="readConfig">Reads the feature flag and <c>core.ignoreCase</c>.</param>
        /// <param name="readConeFile">
        /// Returns the decoded content of the worktree's <c>info/sparse-checkout</c> file (see
        /// <see cref="ConeCoverage.DecodeConeFile"/>), or null when the hook cannot read it.
        /// </param>
        /// <param name="createPipeClient">Creates an unconnected client for the mount pipe.</param>
        /// <param name="hookCurrentDirectory">The current directory of the hook process.</param>
        /// <param name="getWorkingDirectoryRoot">
        /// Returns the root of the worktree that Git runs in, in the same form as
        /// <paramref name="hookCurrentDirectory"/>.
        /// </param>
        /// <param name="gitPrefix">The value of <c>GIT_PREFIX</c>, or null.</param>
        /// <param name="trace">Writes a diagnostic message.</param>
        public ConeManagementHook(
            ReadSparseIndexConfig readConfig,
            Func<string> readConeFile,
            Func<NamedPipeClient> createPipeClient,
            string hookCurrentDirectory,
            Func<string> getWorkingDirectoryRoot,
            string gitPrefix,
            Action<string> trace)
        {
            this.readConfig = readConfig ?? throw new ArgumentNullException(nameof(readConfig));
            this.readConeFile = readConeFile ?? throw new ArgumentNullException(nameof(readConeFile));
            this.createPipeClient = createPipeClient ?? throw new ArgumentNullException(nameof(createPipeClient));
            this.hookCurrentDirectory = hookCurrentDirectory ?? throw new ArgumentNullException(nameof(hookCurrentDirectory));
            this.getWorkingDirectoryRoot = getWorkingDirectoryRoot ?? throw new ArgumentNullException(nameof(getWorkingDirectoryRoot));
            this.gitPrefix = gitPrefix;
            this.trace = trace ?? (message => { });
        }

        /// <summary>
        /// Reads the <c>gvfs.auto-sparse-index</c> flag. When it is true, also reads
        /// <c>core.ignoreCase</c>.
        /// </summary>
        /// <returns>The value of the feature flag.</returns>
        internal delegate bool ReadSparseIndexConfig(out bool ignoreCase);

        /// <summary>
        /// The widen that the hook sent and stopped waiting for (<see cref="ConeRequestOutcome.ReplyTimedOut"/>),
        /// or null. The mount can still run Git for it.
        /// </summary>
        public Task AbandonedRequest { get; private set; }

        /// <summary>
        /// If the command names paths outside the cone, ask the mount to widen the cone. Never
        /// throws.
        /// </summary>
        /// <param name="args">The hook arguments.</param>
        /// <param name="waitBudgetMs">
        /// The maximum time to connect and send the request. If this time ends first, the hook
        /// does not send the request.
        /// </param>
        /// <param name="replyCeilingMs">
        /// The maximum time to wait for the reply after the hook sent the request. The mount
        /// runs Git to apply the widen, and that Git holds the index lock. Thus the hook waits
        /// for the reply, so that the user's Git does not find the index lock.
        /// </param>
        public ConeRequestOutcome TrySendWiden(
            string[] args,
            int waitBudgetMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs,
            int replyCeilingMs = NamedPipeMessages.ConeManagement.WidenReplyCeilingMs)
        {
            ConeRequestOutcome outcome;
            try
            {
                outcome = this.Widen(args, waitBudgetMs, replyCeilingMs);
            }
            catch (Exception e)
            {
                this.trace($"widen failed: {e.Message}");
                return ConeRequestOutcome.Failed;
            }

            this.TraceOutcome("widen", outcome);
            return outcome;
        }

        /// <summary>
        /// If the command names paths, ask the mount to remove the cone entries that it added
        /// for this command, and wait for the reply, at most <paramref name="waitBudgetMs"/>
        /// in total. Never throws.
        /// </summary>
        /// <remarks>
        /// The post-command hook does not repeat the in-cone check of the pre-command hook:
        /// the cone can change while Git runs, so the result could differ. A narrow for a
        /// command that did not widen does nothing. A narrow only writes the cone file and does
        /// not run Git, so the short budget does not let the user's Git find the index lock.
        /// </remarks>
        public ConeRequestOutcome TrySendNarrow(string[] args, int waitBudgetMs = NamedPipeMessages.ConeManagement.NarrowWaitBudgetMs)
        {
            ConeRequestOutcome outcome;
            try
            {
                outcome = this.Narrow(args, waitBudgetMs);
            }
            catch (Exception e)
            {
                this.trace($"narrow failed: {e.Message}");
                return ConeRequestOutcome.Failed;
            }

            this.TraceOutcome("narrow", outcome);
            return outcome;
        }

        /// <summary>
        /// Return the id that pairs the widen and narrow requests of one Git command: the Git
        /// process id from the last <c>--git-pid=</c> argument, which Git appends after the
        /// user's arguments. Return an empty string when it is absent or not a number.
        /// </summary>
        internal static string GetCommandId(string[] args)
        {
            if (args != null)
            {
                for (int i = args.Length - 1; i >= 0; i--)
                {
                    string arg = args[i];
                    if (arg != null && arg.StartsWith(GitPathspecParser.GitPidArgPrefix, StringComparison.Ordinal))
                    {
                        string value = arg.Substring(GitPathspecParser.GitPidArgPrefix.Length);
                        return value.Length != 0 && NamedPipeMessages.ConeManagement.IsValidCommandId(value)
                            ? value
                            : string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Return true when GVFS manages a sparse-index cone for this worktree: the cone file is in
        /// cone mode, and <c>gvfs.auto-sparse-index</c> is true. A widen and a narrow use the same
        /// checks.
        /// </summary>
        public bool IsSparseIndexConeManaged()
        {
            return this.GetUnmanagedOutcome(out _, out _) == null;
        }

        private static bool IsStash(ParsedGitCommand parsed)
        {
            return string.Equals(parsed.Command, StashCommand, StringComparison.Ordinal);
        }

        /// <summary>
        /// Return null when GVFS manages a sparse-index cone, or the outcome that ends a request
        /// when it does not.
        /// </summary>
        private ConeRequestOutcome? GetUnmanagedOutcome(out string coneFileContent, out bool ignoreCase)
        {
            ignoreCase = false;

            // Check the cone file before the config. Git can use a sparse index only in cone
            // mode, and a file read costs much less than the repository open that a config
            // read needs. GVFS does not write a cone-mode file unless the feature is on.
            coneFileContent = this.readConeFile();
            if (!ConeCoverage.HasConeHeader(coneFileContent))
            {
                return ConeRequestOutcome.NoConeFile;
            }

            if (!this.readConfig(out ignoreCase))
            {
                return ConeRequestOutcome.FeatureDisabled;
            }

            return null;
        }

        private ConeRequestOutcome Widen(string[] args, int waitBudgetMs, int replyCeilingMs)
        {
            ParsedGitCommand parsed = GitPathspecParser.ParseHookArgs(args);
            if (!parsed.NamesPaths)
            {
                return ConeRequestOutcome.NoPathsNamed;
            }

            ConeRequestOutcome? unmanaged = this.GetUnmanagedOutcome(out string coneFileContent, out bool ignoreCase);
            if (unmanaged != null)
            {
                return unmanaged.Value;
            }

            // Measured on a large repository: git stash push with a pathspec still expanded the
            // whole index after a widen, and the widen made the command slower.
            if (IsStash(parsed))
            {
                return ConeRequestOutcome.SkippedStash;
            }

            // Git applies -C, --git-dir, and --work-tree before it runs the hook, so the hook
            // does not see them. If they are present anyway, the paths belong to another
            // directory or repository.
            if (!string.IsNullOrEmpty(parsed.ChangeDirectory) ||
                !string.IsNullOrEmpty(parsed.GitDir) ||
                !string.IsNullOrEmpty(parsed.WorkTree))
            {
                return ConeRequestOutcome.NoResolvablePaths;
            }

            string workingDirectoryRoot = this.getWorkingDirectoryRoot();
            string pathspecBaseDirectory = ConePathspecResolver.GetPathspecBaseDirectory(
                this.hookCurrentDirectory,
                workingDirectoryRoot,
                this.gitPrefix);

            IReadOnlyList<string> paths = ConePathspecResolver.ResolveToGitPaths(
                pathspecBaseDirectory,
                workingDirectoryRoot,
                parsed.Pathspecs);

            if (paths.Count == 0)
            {
                return ConeRequestOutcome.NoResolvablePaths;
            }

            // When the hook cannot trust the cone file, it sends every path and the mount
            // decides.
            if (ConeCoverage.TryParseConeFile(coneFileContent, ignoreCase, out ConePatternSet cone))
            {
                paths = new ConeCoverage(cone).GetUncoveredPaths(paths);
                if (paths.Count == 0)
                {
                    return ConeRequestOutcome.SkippedInCone;
                }
            }

            // The mount refuses such a widen every time, so the hook does not send it (see
            // PreCommandConeRequests.WidenLeavesNoGap).
            if (paths.Count > AutoSparseIndexConeManager.MaxWidenPaths)
            {
                return ConeRequestOutcome.TooManyPaths;
            }

            NamedPipeMessages.ConeManagement.WidenParameters parameters =
                new NamedPipeMessages.ConeManagement.WidenParameters(GetCommandId(args), paths);

            return this.SendBounded(
                NamedPipeMessages.ConeManagement.WidenRequest,
                parameters.ToBody(),
                waitBudgetMs,
                replyCeilingMs);
        }

        private ConeRequestOutcome Narrow(string[] args, int waitBudgetMs)
        {
            ParsedGitCommand parsed = GitPathspecParser.ParseHookArgs(args);
            if (!parsed.NamesPaths)
            {
                return ConeRequestOutcome.NoPathsNamed;
            }

            ConeRequestOutcome? unmanaged = this.GetUnmanagedOutcome(out _, out _);
            if (unmanaged != null)
            {
                return unmanaged.Value;
            }

            if (IsStash(parsed))
            {
                return ConeRequestOutcome.SkippedStash;
            }

            NamedPipeMessages.ConeManagement.NarrowParameters parameters =
                new NamedPipeMessages.ConeManagement.NarrowParameters(GetCommandId(args));

            return this.SendBounded(
                NamedPipeMessages.ConeManagement.NarrowRequest,
                parameters.ToBody(),
                waitBudgetMs,
                replyCeilingMs: null);
        }

        private void TraceOutcome(string request, ConeRequestOutcome outcome)
        {
            switch (outcome)
            {
                case ConeRequestOutcome.NoPathsNamed:
                case ConeRequestOutcome.NoConeFile:
                case ConeRequestOutcome.FeatureDisabled:
                case ConeRequestOutcome.Applied:
                    break;

                case ConeRequestOutcome.SkippedStash:
                case ConeRequestOutcome.SkippedInCone:
                case ConeRequestOutcome.NoResolvablePaths:
                case ConeRequestOutcome.TooManyPaths:
                    this.trace($"{request} skipped: {outcome}");
                    break;

                default:
                    this.trace($"{request} not applied: {outcome}");
                    break;
            }
        }

        /// <summary>
        /// Connect, send the request, and read the reply on a worker task.
        /// </summary>
        /// <param name="header">The request header.</param>
        /// <param name="body">The request body.</param>
        /// <param name="waitBudgetMs">
        /// The maximum time to connect and send. If <paramref name="replyCeilingMs"/> is null,
        /// it also bounds the reply: the full request then takes at most this time.
        /// </param>
        /// <param name="replyCeilingMs">
        /// If not null, the maximum time to wait for the reply after the request was sent.
        /// </param>
        private ConeRequestOutcome SendBounded(string header, string body, int waitBudgetMs, int? replyCeilingMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            object sendGate = new object();
            bool sendAbandoned = false;
            bool requestSent = false;
            TaskCompletionSource<bool> sending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<ConeRequestOutcome> task = Task.Run(() =>
            {
                using (NamedPipeClient pipeClient = this.createPipeClient())
                {
                    if (!pipeClient.Connect(NamedPipeMessages.ConeManagement.ConnectTimeoutMs, out _))
                    {
                        return ConeRequestOutcome.MountUnavailable;
                    }

                    // After the budget ends, the hook does not wait for this task. A request sent
                    // after that could make the mount run Git while the user's Git runs.
                    lock (sendGate)
                    {
                        if (sendAbandoned)
                        {
                            return ConeRequestOutcome.TimedOut;
                        }

                        requestSent = true;
                    }

                    sending.TrySetResult(true);
                    pipeClient.SendRequest(new NamedPipeMessages.Message(header, body));
                    NamedPipeMessages.Message response = pipeClient.ReadResponse();
                    switch (response.Header)
                    {
                        case NamedPipeMessages.ConeManagement.SuccessResult:
                            return ConeRequestOutcome.Applied;

                        case NamedPipeMessages.ConeManagement.NotLockHolderResult:
                            return ConeRequestOutcome.NotLockHolder;

                        case NamedPipeMessages.ConeManagement.NotEnabledResult:
                            // The mount has no cone manager, so it never changes the cone.
                            return ConeRequestOutcome.FeatureDisabled;

                        default:
                            return ConeRequestOutcome.Rejected;
                    }
                }
            });

            // If the hook cannot send in time, or the mount does not reply in time, abandon the
            // task. The hook process exits soon after this method returns, which ends the task.
            if (Task.WaitAny(new Task[] { task, sending.Task }, waitBudgetMs) < 0)
            {
                lock (sendGate)
                {
                    if (!requestSent)
                    {
                        sendAbandoned = true;
                        return ConeRequestOutcome.TimedOut;
                    }
                }
            }

            int replyWaitMs = replyCeilingMs ?? Math.Max(0, waitBudgetMs - (int)stopwatch.ElapsedMilliseconds);
            if (!task.Wait(replyWaitMs))
            {
                if (replyCeilingMs.HasValue)
                {
                    this.AbandonedRequest = task;
                    return ConeRequestOutcome.ReplyTimedOut;
                }

                return ConeRequestOutcome.TimedOut;
            }

            return task.Result;
        }
    }
}
