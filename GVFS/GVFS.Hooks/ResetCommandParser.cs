using System;

namespace GVFS.Hooks
{
    /// <summary>
    /// Pure parsing logic for detecting mixed resets and extracting the
    /// target commit. Separated from Program.Reset.cs for testability.
    /// </summary>
    public static class ResetCommandParser
    {
        /// <summary>
        /// Result of parsing a git reset command.
        /// </summary>
        public class ResetParseResult
        {
            /// <summary>Whether this is a mixed reset that needs PrepareForReset handling.</summary>
            public bool IsMixedReset { get; set; }

            /// <summary>
            /// The target commit (ref, SHA, HEAD~N, etc.), or null if resetting to HEAD.
            /// For path-based resets (git reset HEAD -- path), this is the tree-ish.
            /// </summary>
            public string TargetCommit { get; set; }

            /// <summary>Whether this is a path-based reset (git reset [commit] -- paths).</summary>
            public bool HasPaths { get; set; }
        }

        /// <summary>
        /// Determines whether the git reset command is a mixed reset that may be
        /// affected by skip-worktree, and extracts the target commit.
        ///
        /// Mixed resets include:
        ///   git reset HEAD~1          (implicit --mixed)
        ///   git reset --mixed HEAD~1  (explicit --mixed)
        ///   git reset HEAD -- path    (path-based, also affected)
        ///
        /// Not affected:
        ///   git reset --soft HEAD~1   (doesn't touch index)
        ///   git reset --hard HEAD~1   (overwrites working tree, handles skip-worktree)
        /// </summary>
        public static ResetParseResult Parse(string[] args)
        {
            // args[0] = hook type, args[1] = "reset", rest are arguments
            bool isSoft = false;
            bool isHard = false;
            bool isMerge = false;
            bool isKeep = false;
            bool pastDashDash = false;
            string targetCommit = null;
            bool hasPositionalAfterDashDash = false;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("--git-pid="))
                {
                    continue;
                }

                if (arg == "--")
                {
                    pastDashDash = true;
                    continue;
                }

                if (pastDashDash)
                {
                    hasPositionalAfterDashDash = true;
                    continue;
                }

                if (arg.Equals("--soft", StringComparison.OrdinalIgnoreCase))
                {
                    isSoft = true;
                    continue;
                }

                if (arg.Equals("--hard", StringComparison.OrdinalIgnoreCase))
                {
                    isHard = true;
                    continue;
                }

                if (arg.Equals("--mixed", StringComparison.OrdinalIgnoreCase))
                {
                    // Explicit --mixed, no-op since mixed is the default
                    continue;
                }

                if (arg.Equals("--merge", StringComparison.OrdinalIgnoreCase))
                {
                    isMerge = true;
                    continue;
                }

                if (arg.Equals("--keep", StringComparison.OrdinalIgnoreCase))
                {
                    isKeep = true;
                    continue;
                }

                // Skip flags that don't affect mode
                if (arg.StartsWith("-"))
                {
                    continue;
                }

                // First positional argument before -- is the target commit
                if (targetCommit == null)
                {
                    targetCommit = arg;
                }
                else
                {
                    // Second positional argument = path (git reset <commit> <paths>)
                    hasPositionalAfterDashDash = true;
                }
            }

            // Only handle mixed resets. Soft doesn't touch index, hard and
            // merge/keep have their own working tree handling.
            bool isMixedReset = !isSoft && !isHard && !isMerge && !isKeep;

            return new ResetParseResult
            {
                IsMixedReset = isMixedReset,
                TargetCommit = targetCommit,
                HasPaths = hasPositionalAfterDashDash,
            };
        }
    }
}
