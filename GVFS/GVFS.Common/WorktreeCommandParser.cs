using System;
using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// Parses git worktree command arguments from hook args arrays.
    /// Hook args format: [hooktype, "worktree", subcommand, options..., positional args..., --git-pid=N, --exit_code=N]
    /// </summary>
    public static class WorktreeCommandParser
    {
        /// <summary>
        /// Gets the worktree subcommand (add, remove, move, list, etc.) from hook args.
        /// </summary>
        public static string GetSubcommand(string[] args)
        {
            // args[0] = hook type, args[1] = "worktree", args[2+] = subcommand and its args
            for (int i = 2; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                {
                    return args[i].ToLowerInvariant();
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a positional argument from git worktree subcommand args.
        /// For 'add': git worktree add [options] &lt;path&gt; [&lt;commit-ish&gt;]
        /// For 'remove': git worktree remove [options] &lt;worktree&gt;
        /// For 'move': git worktree move [options] &lt;worktree&gt; &lt;new-path&gt;
        /// </summary>
        /// <param name="args">Full hook args array (hooktype, command, subcommand, ...)</param>
        /// <param name="positionalIndex">0-based index of the positional arg after the subcommand</param>
        public static string GetPositionalArg(string[] args, int positionalIndex)
        {
            var optionsWithValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "-b", "-B", "--reason"
            };

            int found = -1;
            bool pastSubcommand = false;
            bool pastSeparator = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--git-pid=") || args[i].StartsWith("--exit_code="))
                {
                    continue;
                }

                if (args[i] == "--")
                {
                    pastSeparator = true;
                    continue;
                }

                if (!pastSeparator && args[i].StartsWith("-"))
                {
                    if (optionsWithValue.Contains(args[i]) && i + 1 < args.Length)
                    {
                        i++;
                    }

                    continue;
                }

                if (!pastSubcommand)
                {
                    pastSubcommand = true;
                    continue;
                }

                found++;
                if (found == positionalIndex)
                {
                    return args[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the first positional argument (worktree path) from git worktree args.
        /// </summary>
        public static string GetPathArg(string[] args)
        {
            return GetPositionalArg(args, 0);
        }
    }
}
