using System;
using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// General-purpose parser for a Git command line as seen by the GVFS
    /// pre/post-command hooks. Its job is to extract the set of working-tree
    /// paths (pathspecs) that a command names, so the mount process can widen
    /// the sparse-index cone to cover them before Git runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is distinct from <see cref="GitCommandLineParser"/>, which classifies
    /// a whole command string into coarse verbs for the virtualization layer.
    /// This parser instead extracts pathspecs from the argv the hooks receive.
    /// </para>
    /// <para>
    /// The hook args array has the shape produced by the GVFS git-hooks loader
    /// plus Git itself:
    /// <c>[hookType, gitCommand, arg0, arg1, ..., --git-pid=&lt;pid&gt;[, --exit_code=&lt;n&gt;]]</c>.
    /// </para>
    /// <para>
    /// Git strips its global options (<c>-C</c>, <c>-c</c>, <c>--git-dir</c>,
    /// <c>--work-tree</c>, ...) in <c>handle_options()</c> before it dispatches
    /// the builtin, so <c>run_pre_command_hook()</c> receives an argv that
    /// starts at the command name. In normal operation the hook therefore never
    /// sees global options. This parser still recognizes a leading run of global
    /// options so that it is a correct general Git command-line parser (and so
    /// callers that construct a full command line are handled), but that path is
    /// defensive: the common case consumes zero global options.
    /// </para>
    /// <para>
    /// Extraction never has to be exact. A missed pathspec only means that Git
    /// expands the sparse index in memory to resolve the path, which is slower
    /// but correct. An over-broad pathspec only makes the cone wider than
    /// necessary. Both error directions are benign, so ambiguous positional
    /// arguments are classified conservatively rather than perfectly.
    /// </para>
    /// </remarks>
    public static class GitPathspecParser
    {
        internal const string GitPidArgPrefix = "--git-pid=";
        private const string ExitCodeArgPrefix = "--exit_code=";
        private const string PathspecFromFileArg = "--pathspec-from-file";
        private const string PathspecFromFileEqualsArg = "--pathspec-from-file=";
        private const string PathspecFileNulArg = "--pathspec-file-nul";
        private const string DashDash = "--";
        private const string EndOfOptions = "--end-of-options";

        // Global (pre-command) long options that consume a following token as
        // their value. Matches git.c handle_options(). The "=value" form of any
        // of these is self-contained and needs no special handling.
        private static readonly HashSet<string> GlobalLongOptionsWithValue = new HashSet<string>(StringComparer.Ordinal)
        {
            "--git-dir",
            "--work-tree",
            "--namespace",
            "--super-prefix",
            "--config-env",
            "--attr-source",
            "-c",
        };

        private static readonly Dictionary<string, GitCommandSpec> CommandSpecs = BuildCommandSpecs();

        private enum PositionalKind
        {
            /// <summary>Every positional argument is a pathspec (add, rm, mv, restore, commit).</summary>
            Pathspec,

            /// <summary>The first <see cref="GitCommandSpec.LeadingRefCount"/> positionals are refs; the rest are pathspecs (checkout, reset).</summary>
            LeadingRefsThenPathspec,

            /// <summary>No positional before "--" is a pathspec; only "-- &lt;paths&gt;" count (diff, log, show, grep, blame, and unknown commands).</summary>
            RefsOnly,

            /// <summary>The command takes no pathspecs at all (switch).</summary>
            None,

            /// <summary>The first positional is a stash subcommand; pathspecs follow only for push/save.</summary>
            StashSubcommand,
        }

        /// <summary>
        /// Parses a hook args array and returns the pathspecs the command names.
        /// </summary>
        /// <param name="hookArgs">
        /// The full hook argument array, where <c>hookArgs[0]</c> is the hook
        /// type and <c>hookArgs[1]</c> is normally the Git command.
        /// </param>
        public static ParsedGitCommand ParseHookArgs(string[] hookArgs)
        {
            if (hookArgs == null || hookArgs.Length < 2)
            {
                return ParsedGitCommand.Empty;
            }

            // hookArgs[0] is the hook type (pre-command / post-command).
            return ParseTokens(hookArgs, startIndex: 1);
        }

        /// <summary>
        /// Parses a token list starting at <paramref name="startIndex"/>, which
        /// must point at the leading global options or the command name.
        /// </summary>
        private static ParsedGitCommand ParseTokens(IReadOnlyList<string> tokens, int startIndex)
        {
            string changeDirectory = null;
            string gitDir = null;
            string workTree = null;

            int index = startIndex;

            // Phase 1: consume a leading run of Git global options. In the normal
            // hook case there are none and this loop exits immediately with the
            // command at the first token.
            while (index < tokens.Count)
            {
                string token = tokens[index];

                if (IsInjectedArg(token))
                {
                    index++;
                    continue;
                }

                if (!token.StartsWith("-", StringComparison.Ordinal) || token == DashDash)
                {
                    // First non-option token is the command name.
                    break;
                }

                index++;

                if (TrySplitEquals(token, out string name, out string value))
                {
                    RecordGlobal(name, value, ref changeDirectory, ref gitDir, ref workTree);
                    continue;
                }

                if (token == "-C")
                {
                    if (index < tokens.Count)
                    {
                        changeDirectory = tokens[index++];
                    }

                    continue;
                }

                if (GlobalLongOptionsWithValue.Contains(token))
                {
                    if (index < tokens.Count)
                    {
                        string globalValue = tokens[index++];
                        RecordGlobal(token, globalValue, ref changeDirectory, ref gitDir, ref workTree);
                    }

                    continue;
                }

                // Boolean global option (or an unrecognized one) — nothing to consume.
            }

            if (index >= tokens.Count)
            {
                return new ParsedGitCommand(null, null, null, false, false, changeDirectory, gitDir, workTree);
            }

            string command = NormalizeCommand(tokens[index]);
            index++;

            GitCommandSpec spec = GetSpec(command);
            return ParseCommandArgs(tokens, index, command, spec, changeDirectory, gitDir, workTree);
        }

        private static ParsedGitCommand ParseCommandArgs(
            IReadOnlyList<string> tokens,
            int startIndex,
            string command,
            GitCommandSpec spec,
            string changeDirectory,
            string gitDir,
            string workTree)
        {
            List<string> pathspecs = new List<string>();
            string pathspecFromFile = null;
            bool pathspecFileNul = false;

            bool pastDashDash = false;
            bool pastEndOfOptions = false;
            bool captureNextAsPathspecFile = false;
            bool skipNextAsValue = false;
            int positionalIndex = 0;
            bool stashTakesPaths = false;

            // Some flags remove the command's leading ref operand, so every
            // positional becomes a pathspec. The canonical case is conflict
            // resolution: "git checkout --ours <path>" / "--theirs <path>" and
            // "git checkout -p <path>" name a working-tree path with no ref, yet
            // the default checkout grammar would misread that first positional as
            // a branch. Detect these up front so classification uses the right
            // leading-ref count regardless of token order.
            int leadingRefCount = spec.LeadingRefCount;
            if (leadingRefCount > 0 && HasRefSuppressingFlag(tokens, startIndex, spec))
            {
                leadingRefCount = 0;
            }

            for (int i = startIndex; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (captureNextAsPathspecFile)
                {
                    pathspecFromFile = token;
                    captureNextAsPathspecFile = false;
                    continue;
                }

                if (skipNextAsValue)
                {
                    skipNextAsValue = false;
                    continue;
                }

                // Git appends --git-pid / --exit_code after all user arguments,
                // so filter them even past "--".
                if (IsInjectedArg(token))
                {
                    continue;
                }

                if (!pastDashDash)
                {
                    if (token == DashDash)
                    {
                        pastDashDash = true;
                        continue;
                    }

                    if (pastEndOfOptions)
                    {
                        // After --end-of-options, a token that starts with "-" is a
                        // positional argument (for example a ref named "-topic"),
                        // but "--" still separates the refs from the pathspecs.
                        ClassifyPositional(spec, leadingRefCount, token, positionalIndex, pathspecs, ref stashTakesPaths);
                        positionalIndex++;
                        continue;
                    }

                    if (token == EndOfOptions)
                    {
                        pastEndOfOptions = true;
                        continue;
                    }

                    if (token.StartsWith(PathspecFromFileEqualsArg, StringComparison.Ordinal))
                    {
                        pathspecFromFile = token.Substring(PathspecFromFileEqualsArg.Length);
                        continue;
                    }

                    if (token == PathspecFromFileArg)
                    {
                        captureNextAsPathspecFile = true;
                        continue;
                    }

                    if (token == PathspecFileNulArg)
                    {
                        pathspecFileNul = true;
                        continue;
                    }

                    if (token.StartsWith("--", StringComparison.Ordinal))
                    {
                        // Long option. The "=value" form is self-contained; the
                        // bare form consumes a following token only when this
                        // command declares the option as value-taking.
                        if (token.IndexOf('=') < 0 && spec.LongOptionsWithValue.Contains(token))
                        {
                            skipNextAsValue = true;
                        }

                        continue;
                    }

                    if (token.Length > 1 && token[0] == '-')
                    {
                        // Short option cluster, possibly combined (e.g. "-am").
                        // A value-taking letter that is the last character
                        // consumes the following token; otherwise the remainder
                        // of the cluster is its baked-in value.
                        if (ShortClusterConsumesNextArg(token, spec.ShortOptionsWithValue))
                        {
                            skipNextAsValue = true;
                        }

                        continue;
                    }

                    ClassifyPositional(spec, leadingRefCount, token, positionalIndex, pathspecs, ref stashTakesPaths);
                    positionalIndex++;
                }
                else
                {
                    // Everything after "--" is a literal pathspec, for commands
                    // that take pathspecs at all.
                    if (PathspecsEnabledPastSeparator(spec, stashTakesPaths))
                    {
                        pathspecs.Add(token);
                    }
                }
            }

            bool failed = pathspecFromFile == "-";

            return new ParsedGitCommand(
                command,
                pathspecs,
                pathspecFromFile,
                pathspecFileNul,
                failed,
                changeDirectory,
                gitDir,
                workTree);
        }

        private static void ClassifyPositional(
            GitCommandSpec spec,
            int leadingRefCount,
            string token,
            int positionalIndex,
            List<string> pathspecs,
            ref bool stashTakesPaths)
        {
            switch (spec.PositionalKind)
            {
                case PositionalKind.Pathspec:
                    pathspecs.Add(token);
                    break;

                case PositionalKind.LeadingRefsThenPathspec:
                    if (positionalIndex >= leadingRefCount)
                    {
                        pathspecs.Add(token);
                    }

                    break;

                case PositionalKind.StashSubcommand:
                    if (positionalIndex == 0)
                    {
                        stashTakesPaths =
                            token.Equals("push", StringComparison.Ordinal) ||
                            token.Equals("save", StringComparison.Ordinal);
                    }
                    else if (stashTakesPaths)
                    {
                        pathspecs.Add(token);
                    }

                    break;

                case PositionalKind.RefsOnly:
                case PositionalKind.None:
                default:
                    break;
            }
        }

        private static bool PathspecsEnabledPastSeparator(GitCommandSpec spec, bool stashTakesPaths)
        {
            switch (spec.PositionalKind)
            {
                case PositionalKind.None:
                    return false;
                case PositionalKind.StashSubcommand:
                    return stashTakesPaths;
                default:
                    return true;
            }
        }

        private static bool ShortClusterConsumesNextArg(string token, HashSet<char> shortOptionsWithValue)
        {
            // token[0] == '-'. Scan the cluster; the first value-taking letter
            // determines the outcome.
            for (int j = 1; j < token.Length; j++)
            {
                if (shortOptionsWithValue.Contains(token[j]))
                {
                    // Last character: value is the following token.
                    // Otherwise: the rest of the cluster is a baked-in value.
                    return j == token.Length - 1;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when any of the command's ref-suppressing flags appears
        /// before a "--" separator. Such a flag means the command has no ref
        /// operand, so every positional is a pathspec.
        /// </summary>
        private static bool HasRefSuppressingFlag(IReadOnlyList<string> tokens, int startIndex, GitCommandSpec spec)
        {
            if (spec.RefSuppressingFlags.Count == 0)
            {
                return false;
            }

            for (int i = startIndex; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (token == DashDash || token == EndOfOptions)
                {
                    // Past "--" or "--end-of-options" a matching token is a
                    // positional argument, not a flag.
                    break;
                }

                if (IsInjectedArg(token))
                {
                    continue;
                }

                if (spec.RefSuppressingFlags.Contains(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInjectedArg(string token)
        {
            return token.StartsWith(GitPidArgPrefix, StringComparison.Ordinal)
                || token.StartsWith(ExitCodeArgPrefix, StringComparison.Ordinal);
        }

        private static bool TrySplitEquals(string token, out string name, out string value)
        {
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex < 0)
            {
                name = null;
                value = null;
                return false;
            }

            name = token.Substring(0, equalsIndex);
            value = token.Substring(equalsIndex + 1);
            return true;
        }

        private static void RecordGlobal(
            string name,
            string value,
            ref string changeDirectory,
            ref string gitDir,
            ref string workTree)
        {
            switch (name)
            {
                case "-C":
                    changeDirectory = value;
                    break;
                case "--git-dir":
                    gitDir = value;
                    break;
                case "--work-tree":
                    workTree = value;
                    break;
            }
        }

        private static string NormalizeCommand(string command)
        {
            string normalized = command.ToLowerInvariant();
            if (normalized.StartsWith("git-", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(4);
            }

            return normalized;
        }

        private static GitCommandSpec GetSpec(string command)
        {
            if (CommandSpecs.TryGetValue(command, out GitCommandSpec spec))
            {
                return spec;
            }

            // Unknown command: trust only an explicit "--" pathspec separator.
            return GitCommandSpec.RefsOnlyDefault;
        }

        private static Dictionary<string, GitCommandSpec> BuildCommandSpecs()
        {
            Dictionary<string, GitCommandSpec> specs = new Dictionary<string, GitCommandSpec>(StringComparer.Ordinal);

            GitCommandSpec pathspecPositional = new GitCommandSpec(PositionalKind.Pathspec);
            specs["add"] = pathspecPositional;
            specs["stage"] = pathspecPositional;
            specs["rm"] = new GitCommandSpec(PositionalKind.Pathspec);
            specs["mv"] = new GitCommandSpec(PositionalKind.Pathspec);

            specs["restore"] = new GitCommandSpec(
                PositionalKind.Pathspec,
                longOptionsWithValue: new[] { "--source", "--conflict" },
                shortOptionsWithValue: new[] { 's' });

            specs["commit"] = new GitCommandSpec(
                PositionalKind.Pathspec,
                longOptionsWithValue: new[]
                {
                    "--message", "--author", "--date", "--reuse-message", "--reedit-message",
                    "--fixup", "--squash", "--file", "--template", "--cleanup", "--trailer",
                },
                shortOptionsWithValue: new[] { 'm', 'C', 'c', 'F', 't' });

            specs["checkout"] = new GitCommandSpec(
                PositionalKind.LeadingRefsThenPathspec,
                leadingRefCount: 1,
                longOptionsWithValue: new[] { "--conflict", "--orphan" },
                shortOptionsWithValue: new[] { 'b', 'B' },
                refSuppressingFlags: new[] { "--ours", "--theirs", "--patch", "-p" });

            specs["switch"] = new GitCommandSpec(
                PositionalKind.None,
                longOptionsWithValue: new[] { "--orphan" },
                shortOptionsWithValue: new[] { 'c', 'C' });

            specs["reset"] = new GitCommandSpec(
                PositionalKind.LeadingRefsThenPathspec,
                leadingRefCount: 1);

            specs["stash"] = new GitCommandSpec(
                PositionalKind.StashSubcommand,
                longOptionsWithValue: new[] { "--message" },
                shortOptionsWithValue: new[] { 'm' });

            // Commands whose paths are only unambiguous after "--".
            specs["diff"] = GitCommandSpec.RefsOnlyDefault;
            specs["log"] = GitCommandSpec.RefsOnlyDefault;
            specs["show"] = GitCommandSpec.RefsOnlyDefault;
            specs["grep"] = GitCommandSpec.RefsOnlyDefault;
            specs["blame"] = GitCommandSpec.RefsOnlyDefault;

            return specs;
        }

        /// <summary>
        /// Immutable per-command grammar description used by the parser.
        /// </summary>
        private sealed class GitCommandSpec
        {
            private static readonly HashSet<string> EmptyLongOptions = new HashSet<string>(StringComparer.Ordinal);
            private static readonly HashSet<char> EmptyShortOptions = new HashSet<char>();

            public static readonly GitCommandSpec RefsOnlyDefault = new GitCommandSpec(PositionalKind.RefsOnly);

            public GitCommandSpec(
                PositionalKind positionalKind,
                int leadingRefCount = 0,
                IEnumerable<string> longOptionsWithValue = null,
                IEnumerable<char> shortOptionsWithValue = null,
                IEnumerable<string> refSuppressingFlags = null)
            {
                this.PositionalKind = positionalKind;
                this.LeadingRefCount = leadingRefCount;
                this.LongOptionsWithValue = longOptionsWithValue == null
                    ? EmptyLongOptions
                    : new HashSet<string>(longOptionsWithValue, StringComparer.Ordinal);
                this.ShortOptionsWithValue = shortOptionsWithValue == null
                    ? EmptyShortOptions
                    : new HashSet<char>(shortOptionsWithValue);
                this.RefSuppressingFlags = refSuppressingFlags == null
                    ? EmptyLongOptions
                    : new HashSet<string>(refSuppressingFlags, StringComparer.Ordinal);
            }

            public PositionalKind PositionalKind { get; }

            public int LeadingRefCount { get; }

            public HashSet<string> LongOptionsWithValue { get; }

            public HashSet<char> ShortOptionsWithValue { get; }

            public HashSet<string> RefSuppressingFlags { get; }
        }
    }
}
