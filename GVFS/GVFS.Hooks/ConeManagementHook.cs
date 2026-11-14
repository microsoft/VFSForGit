using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using System;
using System.Collections.Generic;
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
    /// Cone management must never block or fail a Git command. Every failure, including no
    /// mount, a mount that does not know the request, a slow mount, and an exception, lets
    /// Git run. Git is then slower, but correct.
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
        /// If the command names paths outside the cone, ask the mount to widen the cone and wait
        /// for the reply, at most <paramref name="waitBudgetMs"/>. Never throws.
        /// </summary>
        public ConeRequestOutcome TrySendWiden(string[] args, int waitBudgetMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs)
        {
            ConeRequestOutcome outcome;
            try
            {
                outcome = this.Widen(args, waitBudgetMs);
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
        /// for this command, and wait for the reply, at most <paramref name="waitBudgetMs"/>.
        /// Never throws.
        /// </summary>
        /// <remarks>
        /// The post-command hook does not repeat the in-cone check of the pre-command hook:
        /// the cone can change while Git runs, so the result could differ. A narrow for a
        /// command that did not widen does nothing.
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

        private static bool IsStash(ParsedGitCommand parsed)
        {
            return string.Equals(parsed.Command, StashCommand, StringComparison.Ordinal);
        }

        private ConeRequestOutcome Widen(string[] args, int waitBudgetMs)
        {
            ParsedGitCommand parsed = GitPathspecParser.ParseHookArgs(args);
            if (!parsed.NamesPaths)
            {
                return ConeRequestOutcome.NoPathsNamed;
            }

            // Check the cone file before the config. Git can use a sparse index only in cone
            // mode, and a file read costs much less than the repository open that a config
            // read needs. GVFS does not write a cone-mode file unless the feature is on.
            string coneFileContent = this.readConeFile();
            if (!ConeCoverage.HasConeHeader(coneFileContent))
            {
                return ConeRequestOutcome.NoConeFile;
            }

            if (!this.readConfig(out bool ignoreCase))
            {
                return ConeRequestOutcome.FeatureDisabled;
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

            NamedPipeMessages.ConeManagement.WidenParameters parameters =
                new NamedPipeMessages.ConeManagement.WidenParameters(GetCommandId(args), paths);

            return this.SendBounded(
                NamedPipeMessages.ConeManagement.WidenRequest,
                parameters.ToBody(),
                waitBudgetMs);
        }

        private ConeRequestOutcome Narrow(string[] args, int waitBudgetMs)
        {
            ParsedGitCommand parsed = GitPathspecParser.ParseHookArgs(args);
            if (!parsed.NamesPaths)
            {
                return ConeRequestOutcome.NoPathsNamed;
            }

            if (!ConeCoverage.HasConeHeader(this.readConeFile()))
            {
                return ConeRequestOutcome.NoConeFile;
            }

            if (!this.readConfig(out _))
            {
                return ConeRequestOutcome.FeatureDisabled;
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
                waitBudgetMs);
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
                    this.trace($"{request} skipped: {outcome}");
                    break;

                default:
                    this.trace($"{request} not applied: {outcome}");
                    break;
            }
        }

        private ConeRequestOutcome SendBounded(string header, string body, int waitBudgetMs)
        {
            Task<ConeRequestOutcome> task = Task.Run(() =>
            {
                using (NamedPipeClient pipeClient = this.createPipeClient())
                {
                    if (!pipeClient.Connect(NamedPipeMessages.ConeManagement.ConnectTimeoutMs, out _))
                    {
                        return ConeRequestOutcome.MountUnavailable;
                    }

                    pipeClient.SendRequest(new NamedPipeMessages.Message(header, body));
                    NamedPipeMessages.Message response = pipeClient.ReadResponse();
                    return response.Header == NamedPipeMessages.ConeManagement.SuccessResult
                        ? ConeRequestOutcome.Applied
                        : ConeRequestOutcome.Rejected;
                }
            });

            // If the mount does not reply in time, abandon the task. The hook process exits
            // soon after this method returns, which ends the task.
            if (!task.Wait(waitBudgetMs))
            {
                return ConeRequestOutcome.TimedOut;
            }

            return task.Result;
        }
    }
}
