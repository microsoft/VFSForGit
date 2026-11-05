using GVFS.Common;
using System;
using System.Linq;

namespace GVFS.Hooks
{
    public partial class Program
    {
        // Hooks run on the git command path, so config reads retry at most once.
        private const int ConfigReadMaxAttempts = 2;

        // The lock serializes changes to the enlistment's index and tracked working tree with GVFS projection updates.
        // Before adding an exemption, check all supported options, subcommands, and child-command paths.
        // Ref and object writes alone do not require this lock. Index refreshes do.
        // Keep arbitrary Git dispatchers locked because nested command hooks can be suppressed.
        // Excluded readers can observe concurrent working-tree changes. They do not receive snapshot isolation.
        internal static bool TryShouldLock(
            string[] args,
            Func<ProcessResult> readGitAliasKeys,
            string searchPath,
            Func<string, uint> getFileAttributes,
            out bool shouldLock,
            out string error)
        {
            error = null;
            string gitCommand = GetGitCommand(args);

            // Git reports a built-in by its name. A git- prefix means that Git found no built-in
            // and will run a program or expand an alias, so the read-only list does not apply.
            // git-submodule and git-web--browse are programs that Git ships in its exec-path,
            // which Git puts first on PATH. They keep their exemptions.
            if (args[1].StartsWith("git-", StringComparison.Ordinal) &&
                gitCommand != "submodule" &&
                gitCommand != "web--browse")
            {
                shouldLock = true;
                if (!GitCommandResolver.TryIsAlias(args[1], readGitAliasKeys, searchPath, getFileAttributes, out bool isAlias, out error))
                {
                    return false;
                }

                shouldLock = !isAlias;
                return true;
            }

            shouldLock = ShouldLockBuiltIn(gitCommand, args);
            return true;
        }

        private static bool ShouldLockBuiltIn(string gitCommand, string[] args)
        {
            switch (gitCommand)
            {
                // Keep these alphabetically sorted
                case "annotate":
                case "blame":
                case "branch":
                case "cat-file":
                case "check-attr":
                case "check-ignore":
                case "check-mailmap":
                case "check-ref-format":
                case "cherry":
                case "commit-graph":
                case "config":
                case "count-objects":
                case "credential":
                case "diff":
                case "diff-files":
                case "diff-index":
                case "diff-tree":
                case "difftool":
                case "fetch":
                case "for-each-ref":
                case "help":
                case "hash-object":
                case "index-pack":
                case "log":
                case "ls-files":
                case "ls-remote":
                case "ls-tree":
                case "merge-base":
                case "multi-pack-index":
                case "name-rev":
                case "pack-objects":
                case "push":
                case "range-diff":
                case "remote":
                case "rev-list":
                case "rev-parse":
                case "shortlog":
                case "show":
                case "show-branch":
                case "show-ref":
                case "symbolic-ref":
                case "tag":
                case "unpack-objects":
                case "update-ref":
                case "verify-commit":
                case "verify-pack":
                case "verify-tag":
                case "version":
                case "web--browse":
                    return false;

                case "worktree":
                    return WorktreeCommandParser.GetSubcommand(args) != "list";

                /*
                 * There are several git commands that are "unsupported" in virtualized (VFS4G)
                 * enlistments that are blocked by git. Usually, these are blocked before they acquire
                 * a GVFSLock, but the submodule command is different, and is blocked after acquiring the
                 * GVFS lock. This can cause issues if another action is attempting to create placeholders.
                 * As we know the submodule command is a no-op, allow it to proceed without acquiring the
                 * GVFSLock. Issue #1164 documents the need for git to block all unsupported commands
                 * before calling the pre-command hook.
                 */
                case "submodule":
                    return false;
            }

            if (gitCommand == "reset" && args.Contains("--soft"))
            {
                return false;
            }

            return true;
        }

        private static string GetGitCommand(string[] args)
        {
            string command = args[1].ToLowerInvariant();
            if (command.StartsWith("git-"))
            {
                command = command.Substring(4);
            }

            return command;
        }
    }
}
