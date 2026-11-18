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
    /// not see a local change to such a file). Read-only commands and plumbing run, because they
    /// do not lose a change: Git expands the index in memory.
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
                case "switch":
                    return true;

                case "apply":
                    return HasOption(args, "--index", "--cached", "--3way", "-3");

                case "commit":
                    return CommitReadsTheWorkingTree(args);

                case "reset":
                    // A mixed reset is an unstage command. A soft reset changes neither the index
                    // nor the working tree.
                    return HasOption(args, "--hard", "--merge", "--keep");

                case "stash":
                    string subcommand = UserArguments(args).FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));
                    return subcommand != "list" && subcommand != "show";

                default:
                    return false;
            }
        }

        /// <summary>
        /// Return true when a commit takes files from the working tree: <c>-a</c>, <c>-i</c>,
        /// <c>-o</c>, <c>-p</c>, <c>--interactive</c>, or paths. A commit of the staged files only
        /// does not read a change that is not staged.
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
                    // A cluster of short options. The rest of the cluster after an option that
                    // takes a value is that value.
                    for (int i = 1; i < arg.Length; i++)
                    {
                        char option = arg[i];
                        if (option == 'a' || option == 'i' || option == 'o' || option == 'p')
                        {
                            return true;
                        }

                        if ("mFcCt".IndexOf(option) >= 0)
                        {
                            skipNext = i == arg.Length - 1;
                            break;
                        }
                    }

                    continue;
                }

                return true;
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
