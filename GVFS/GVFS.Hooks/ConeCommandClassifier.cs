using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Hooks
{
    /// <summary>
    /// What a failed cone request means for a Git command (see <see cref="PreCommandConeRequests"/>).
    /// </summary>
    internal enum ConeCommandKind
    {
        /// <summary>
        /// The command does not write the index from working-tree changes, or it is plumbing. A
        /// failed request only makes Git slower, so Git always runs.
        /// </summary>
        Other,

        /// <summary>
        /// The command writes the index, and it is wrong when the cone does not include the paths
        /// that it names or the working-tree changes that it reads. When a request fails, the hook
        /// stops the command before Git runs.
        /// </summary>
        WritesIndex,

        /// <summary>
        /// The command unstages files (see <see cref="UnstageCommandParser.ShouldPrepareForUnstage"/>).
        /// </summary>
        Unstage,
    }

    /// <summary>
    /// Classifies a Git command for the pre-command cone requests.
    /// </summary>
    /// <remarks>
    /// With a sparse index, Git has no index entry for a file inside a directory that the cone
    /// does not include. When the mount could not widen the cone or apply a pending change, the
    /// commands below then fail (for example <c>git add</c> reports that the path is outside the
    /// sparse-checkout definition), or they run without the change and report success (for
    /// example <c>git stash</c>, <c>git commit -a</c>, and <c>git checkout &lt;branch&gt;</c> do
    /// not see a local change to such a file). Read-only commands and plumbing run. They do not
    /// change the index from the working tree, and the hook warns when they can miss a change.
    /// </remarks>
    internal static class ConeCommandClassifier
    {
        /// <param name="command">The Git command, in lowercase, without a "git-" prefix.</param>
        /// <param name="args">The hook arguments: the hook type, the command, and the command's arguments.</param>
        /// <param name="isSparseIndexConeManaged">Returns true when GVFS manages a sparse-index cone.</param>
        public static ConeCommandKind Classify(string command, string[] args, Func<bool> isSparseIndexConeManaged)
        {
            ArgumentNullException.ThrowIfNull(args);

            switch (command)
            {
                case "restore":
                case "checkout":
                case "reset":
                    if (UnstageCommandParser.ShouldPrepareForUnstage(command, args, isSparseIndexConeManaged))
                    {
                        return ConeCommandKind.Unstage;
                    }

                    break;
            }

            return WritesIndex(command, args) ? ConeCommandKind.WritesIndex : ConeCommandKind.Other;
        }

        private static bool WritesIndex(string command, string[] args)
        {
            switch (command)
            {
                case "add":
                case "am":
                case "checkout":
                case "cherry-pick":
                case "merge":
                case "mv":
                case "pull":
                case "rebase":
                case "restore":
                case "revert":
                case "rm":
                case "stage":
                case "switch":
                    return true;

                case "apply":
                    return ApplyWritesTheIndex(args);

                case "commit":
                    return CommitReadsTheWorkingTree(args);

                case "reset":
                    // A mixed reset is an unstage command. A soft reset changes neither the index
                    // nor the working tree.
                    return HasOption(args, "--hard", "--merge", "--keep");

                case "stash":
                    return StashReadsOrWritesTheWorkingTree(args);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Return true when <c>git apply</c> uses the index (<c>--index</c>, <c>--cached</c>, or
        /// <c>--3way</c>) and applies the patch. With <c>--check</c>, <c>--stat</c>,
        /// <c>--numstat</c>, or <c>--summary</c>, and without <c>--apply</c>, it only reads.
        /// </summary>
        private static bool ApplyWritesTheIndex(string[] args)
        {
            if (!HasOption(args, "--index", "--cached", "--3way") && !HasShortOption(args, '3', valueOptions: "pC"))
            {
                return false;
            }

            return HasOption(args, "--apply") || !HasOption(args, "--check", "--stat", "--numstat", "--summary");
        }

        /// <summary>
        /// Return true unless the stash subcommand only reads or changes the stash list
        /// (<c>list</c>, <c>show</c>, <c>drop</c>, <c>clear</c>, <c>store</c>). A stash without a
        /// subcommand, or with <c>--</c> before the first argument, is <c>stash push</c>.
        /// </summary>
        private static bool StashReadsOrWritesTheWorkingTree(string[] args)
        {
            string subcommand = null;
            bool skipNext = false;
            foreach (string arg in UserArguments(args))
            {
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (arg == "--")
                {
                    break;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    subcommand = arg;
                    break;
                }

                // The message of an implicit "stash push".
                skipNext = arg == "-m" || arg == "--message";
            }

            switch (subcommand)
            {
                case "list":
                case "show":
                case "drop":
                case "clear":
                case "store":
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Return true when a commit takes files from the working tree: <c>-a</c>/<c>--all</c>,
        /// <c>-i</c>/<c>--include</c>, <c>-o</c>/<c>--only</c>, <c>-p</c>/<c>--patch</c>,
        /// <c>--interactive</c>, <c>--pathspec-from-file</c>, or paths. A commit of the staged
        /// files only does not read a change that is not staged. Abbreviated long options are not
        /// recognized.
        /// </summary>
        private static bool CommitReadsTheWorkingTree(string[] args)
        {
            bool pastDashDash = false;
            bool skipNext = false;
            foreach (string arg in UserArguments(args))
            {
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (pastDashDash)
                {
                    return true;
                }

                if (arg == "--")
                {
                    pastDashDash = true;
                    continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string option = arg.Split('=')[0];
                    switch (option)
                    {
                        case "--all":
                        case "--include":
                        case "--only":
                        case "--patch":
                        case "--interactive":
                        case "--pathspec-from-file":
                            return true;

                        case "--message":
                        case "--file":
                        case "--reuse-message":
                        case "--reedit-message":
                        case "--template":
                        case "--author":
                        case "--date":
                        case "--fixup":
                        case "--squash":
                        case "--trailer":
                        case "--cleanup":
                            skipNext = !arg.Contains('=');
                            break;
                    }

                    continue;
                }

                if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    if (ShortClusterHas(arg, "aiop", valueOptions: "mFcCt", attachedValueOptions: "Su", out skipNext))
                    {
                        return true;
                    }

                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Return true when the cluster of short options <paramref name="arg"/> (for example
        /// <c>-qam</c>) has one of <paramref name="flags"/>. The rest of the cluster after a letter
        /// in <paramref name="valueOptions"/> or <paramref name="attachedValueOptions"/> is the
        /// value of that option, not more options.
        /// </summary>
        /// <param name="valueOptions">Options that take a value, attached or in the next argument.</param>
        /// <param name="attachedValueOptions">Options that take an optional value, which must be attached.</param>
        /// <param name="valueInNextArgument">True when the value of the last option is the next argument.</param>
        private static bool ShortClusterHas(string arg, string flags, string valueOptions, string attachedValueOptions, out bool valueInNextArgument)
        {
            valueInNextArgument = false;
            for (int i = 1; i < arg.Length; i++)
            {
                char option = arg[i];
                if (flags.IndexOf(option) >= 0)
                {
                    return true;
                }

                if (valueOptions.IndexOf(option) >= 0)
                {
                    valueInNextArgument = i == arg.Length - 1;
                    return false;
                }

                if (attachedValueOptions.IndexOf(option) >= 0)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool HasShortOption(string[] args, char flag, string valueOptions)
        {
            bool skipNext = false;
            foreach (string arg in UserArguments(args).TakeWhile(arg => arg != "--"))
            {
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (arg.Length > 1 && arg[0] == '-' && arg[1] != '-' &&
                    ShortClusterHas(arg, flag.ToString(), valueOptions, attachedValueOptions: string.Empty, out skipNext))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOption(string[] args, params string[] options)
        {
            return UserArguments(args).TakeWhile(arg => arg != "--").Any(arg => options.Contains(arg.Split('=')[0]));
        }

        private static IEnumerable<string> UserArguments(string[] args)
        {
            return args.Skip(2).Where(arg => arg != null && !arg.StartsWith("--git-pid=", StringComparison.Ordinal));
        }
    }
}
