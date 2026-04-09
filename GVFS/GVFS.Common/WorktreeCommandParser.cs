using System;
using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// Parses git worktree command arguments from hook args arrays.
    /// Hook args format: [hooktype, "worktree", subcommand, options..., positional args..., --git-pid=N, --exit_code=N]
    ///
    /// Assumptions:
    /// - Args are passed by git exactly as the user typed them (no normalization).
    /// - --git-pid and --exit_code are always appended by git in =value form.
    /// - Single-letter flags may be combined (e.g., -fd for --force --detach).
    /// - -b/-B always consume the next arg as a branch name, even when combined (e.g., -fb branch).
    ///
    /// Future improvement: consider replacing with a POSIX-compatible arg parser
    /// library (e.g., Mono.Options, MIT license) to handle edge cases more robustly.
    /// </summary>
    public static class WorktreeCommandParser
    {
        private static readonly HashSet<char> ShortOptionsWithValue = new HashSet<char> { 'b', 'B' };

        /// <summary>
        /// Gets the worktree subcommand (add, remove, move, list, etc.) from hook args.
        /// </summary>
        public static string GetSubcommand(string[] args)
        {
            // args[0] = hook type, args[1] = "worktree", args[2+] = subcommand and its args
            for (int i = 2; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-"))
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
            var longOptionsWithValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "--reason"
            };

            int found = -1;
            bool pastSubcommand = false;
            bool pastSeparator = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--git-pid") || args[i].StartsWith("--exit_code"))
                {
                    // Always =value form, but skip either way
                    if (!args[i].Contains("=") && i + 1 < args.Length)
                    {
                        i++;
                    }

                    continue;
                }

                if (args[i] == "--")
                {
                    pastSeparator = true;
                    continue;
                }

                if (!pastSeparator && args[i].StartsWith("--"))
                {
                    // Long option — check if it takes a separate value
                    if (longOptionsWithValue.Contains(args[i]) && i + 1 < args.Length)
                    {
                        i++;
                    }

                    continue;
                }

                if (!pastSeparator && args[i].StartsWith("-") && args[i].Length > 1)
                {
                    // Short option(s), possibly combined (e.g., -fd, -fb branch).
                    // A value-taking letter consumes the rest of the arg as its value.
                    // Only consume the next arg if the first value-taking letter is
                    // the last character (no baked-in value).
                    // e.g., -bfd → b="fd" (baked), -fdb val → f,d booleans, b="val"
                    //        -Bb → B="b" (baked), -fBb → f boolean, B="b" (baked)
                    string flags = args[i].Substring(1);
                    bool consumesNextArg = false;
                    for (int j = 0; j < flags.Length; j++)
                    {
                        if (ShortOptionsWithValue.Contains(flags[j]))
                        {
                            // This letter takes a value. If it's the last letter,
                            // the value is the next arg. Otherwise the value is the
                            // remaining characters (baked in) and we're done.
                            consumesNextArg = (j == flags.Length - 1);
                            break;
                        }
                    }

                    if (consumesNextArg && i + 1 < args.Length)
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
