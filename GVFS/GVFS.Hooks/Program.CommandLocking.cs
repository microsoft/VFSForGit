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
        // Ref and object writes alone do not require this lock.
        // Existing diff exemptions allow opportunistic stat-cache refreshes.
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
                case "bundle":
                case "cat-file":
                case "check-attr":
                case "check-ignore":
                case "check-mailmap":
                case "check-ref-format":
                case "cherry":
                case "column":
                case "commit-graph":
                case "commit-tree":
                case "config":
                case "count-objects":
                case "credential":
                case "diff":
                case "diff-files":
                case "diff-index":
                case "diff-pairs":
                case "diff-tree":
                case "difftool":
                case "fetch":
                case "fmt-merge-msg":
                case "for-each-ref":
                case "format-patch":
                case "format-rev":
                case "get-tar-commit-id":
                case "help":
                case "hash-object":
                case "index-pack":
                case "last-modified":
                case "log":
                case "ls-files":
                case "ls-remote":
                case "ls-tree":
                case "merge-base":
                case "merge-tree":
                case "mktag":
                case "mktree":
                case "multi-pack-index":
                case "name-rev":
                case "notes":
                case "pack-objects":
                case "pack-refs":
                case "patch-id":
                case "pickaxe":
                case "push":
                case "range-diff":
                case "reflog":
                case "remote":
                case "replace":
                case "rev-list":
                case "rev-parse":
                case "shortlog":
                case "show":
                case "show-branch":
                case "show-index":
                case "show-ref":
                case "stripspace":
                case "symbolic-ref":
                case "tag":
                case "unpack-objects":
                case "update-ref":
                case "update-server-info":
                case "url-parse":
                case "var":
                case "verify-commit":
                case "verify-pack":
                case "verify-tag":
                case "version":
                case "web--browse":
                    return false;

                case "describe":
                    return !HasOnlySafeOptions(args, GetDescribeOptionType, out _);

                case "grep":
                    return !HasOnlySafeOptions(args, GetGrepOptionType, out _);

                case "merge-file":
                    return !HasOnlySafeOptions(args, GetMergeFileOptionType, out bool hasReadOnlyMode) || !hasReadOnlyMode;

                case "refs":
                    switch (args.Length > 2 ? args[2] : null)
                    {
                        case "list":
                        case "exists":
                        case "verify":
                        case "optimize":
                        case "create":
                        case "delete":
                        case "update":
                        case "rename":
                            return false;
                    }

                    return true;

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

        private static bool HasOnlySafeOptions(string[] args, Func<string, CommandOptionType> getOptionType, out bool hasReadOnlyMode)
        {
            hasReadOnlyMode = false;
            int end = args.Length;

            // Only the appended hook metadata is excluded. User arguments with these names remain unknown options.
            if (end > 2 && args[0] == "post-command" && args[end - 1].StartsWith("--exit_code=", StringComparison.Ordinal))
            {
                --end;
            }

            if (end > 2 && args[end - 1].StartsWith("--git-pid=", StringComparison.Ordinal))
            {
                --end;
            }

            for (int i = 2; i < end; ++i)
            {
                string argument = args[i];
                if (argument == "--")
                {
                    return true;
                }

                if (!argument.StartsWith("-", StringComparison.Ordinal) || argument == "-")
                {
                    continue;
                }

                if (argument.StartsWith("--", StringComparison.Ordinal))
                {
                    int equals = argument.IndexOf('=');
                    string option = equals < 0 ? argument : argument.Substring(0, equals);
                    CommandOptionType type = getOptionType(option);
                    if (type == CommandOptionType.Unknown ||
                        (equals >= 0 && type != CommandOptionType.Value && type != CommandOptionType.OptionalValue))
                    {
                        return false;
                    }

                    hasReadOnlyMode |= type == CommandOptionType.ReadOnlyMode;
                    if (type == CommandOptionType.Value && equals < 0 && ++i >= end)
                    {
                        return false;
                    }
                }
                else
                {
                    for (int j = 1; j < argument.Length; ++j)
                    {
                        CommandOptionType type = getOptionType("-" + argument[j]);
                        if (type == CommandOptionType.Unknown)
                        {
                            return false;
                        }

                        hasReadOnlyMode |= type == CommandOptionType.ReadOnlyMode;
                        if (type == CommandOptionType.Value || type == CommandOptionType.OptionalValue)
                        {
                            if (type == CommandOptionType.Value && j == argument.Length - 1 && ++i >= end)
                            {
                                return false;
                            }

                            break;
                        }
                    }
                }
            }

            return true;
        }

        private static CommandOptionType GetDescribeOptionType(string option)
        {
            switch (option)
            {
                case "--contains":
                case "--debug":
                case "--all":
                case "--tags":
                case "--long":
                case "--first-parent":
                case "--exact-match":
                case "--always":
                    return CommandOptionType.Flag;

                case "--candidates":
                case "--match":
                case "--exclude":
                    return CommandOptionType.Value;

                case "--abbrev":
                    return CommandOptionType.OptionalValue;
            }

            return CommandOptionType.Unknown;
        }

        private static CommandOptionType GetGrepOptionType(string option)
        {
            switch (option)
            {
                case "-v":
                case "-i":
                case "-w":
                case "-a":
                case "-I":
                case "-r":
                case "-E":
                case "-G":
                case "-F":
                case "-P":
                case "-n":
                case "-h":
                case "-H":
                case "-l":
                case "-L":
                case "-z":
                case "-o":
                case "-c":
                case "-p":
                case "-W":
                case "-q":
                case "--cached":
                case "--no-index":
                case "--untracked":
                case "--exclude-standard":
                case "--recurse-submodules":
                case "--invert-match":
                case "--ignore-case":
                case "--word-regexp":
                case "--text":
                case "--textconv":
                case "--recursive":
                case "--extended-regexp":
                case "--basic-regexp":
                case "--fixed-strings":
                case "--perl-regexp":
                case "--line-number":
                case "--column":
                case "--full-name":
                case "--files-with-matches":
                case "--name-only":
                case "--files-without-match":
                case "--null":
                case "--only-matching":
                case "--count":
                case "--break":
                case "--heading":
                case "--show-function":
                case "--function-context":
                case "--and":
                case "--or":
                case "--not":
                case "--quiet":
                case "--all-match":
                    return CommandOptionType.Flag;

                case "-e":
                case "-f":
                case "-A":
                case "-B":
                case "-C":
                case "-m":
                case "--max-depth":
                case "--context":
                case "--before-context":
                case "--after-context":
                case "--threads":
                case "--max-count":
                    return CommandOptionType.Value;

                case "--color":
                    return CommandOptionType.OptionalValue;
            }

            return CommandOptionType.Unknown;
        }

        private static CommandOptionType GetMergeFileOptionType(string option)
        {
            switch (option)
            {
                case "-p":
                case "--stdout":
                case "--object-id":
                    return CommandOptionType.ReadOnlyMode;

                case "-q":
                case "--quiet":
                case "--diff3":
                case "--zdiff3":
                case "--ours":
                case "--theirs":
                case "--union":
                    return CommandOptionType.Flag;

                case "-L":
                case "--diff-algorithm":
                case "--marker-size":
                    return CommandOptionType.Value;
            }

            return CommandOptionType.Unknown;
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

        private enum CommandOptionType
        {
            Unknown,
            Flag,
            Value,
            OptionalValue,
            ReadOnlyMode,
        }
    }
}
