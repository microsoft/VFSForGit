using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Hooks
{
    /// <summary>
    /// Pure parsing logic for detecting and extracting pathspecs from
    /// git unstage commands. Separated from Program.Unstage.cs so it
    /// can be linked into the unit test project without pulling in the
    /// rest of the Hooks assembly.
    /// </summary>
    public static class UnstageCommandParser
    {
        /// <summary>
        /// Result of parsing pathspec arguments from a git unstage command.
        /// </summary>
        public class PathspecResult
        {
            /// <summary>Null-separated inline pathspecs, or empty for all staged files.</summary>
            public string InlinePathspecs { get; set; }

            /// <summary>Path to a --pathspec-from-file, or null if not specified.</summary>
            public string PathspecFromFile { get; set; }

            /// <summary>Whether --pathspec-file-nul was specified.</summary>
            public bool PathspecFileNul { get; set; }

            /// <summary>True if parsing failed and the command should be blocked.</summary>
            public bool Failed { get; set; }
        }

        /// <summary>
        /// Detects whether the git command is an unstage operation that may need
        /// special handling for VFS projections.
        /// Matches: "restore --staged", "restore -S", "checkout HEAD --"
        /// </summary>
        public static bool IsUnstageOperation(string command, string[] args)
        {
            if (command == "restore")
            {
                return args.Any(arg =>
                    arg.Equals("--staged", StringComparison.OrdinalIgnoreCase) ||
                    // -S is --staged; char overload of IndexOf is case-sensitive,
                    // which is required because lowercase -s means --source
                    (arg.StartsWith("-") && !arg.StartsWith("--") && arg.IndexOf('S') >= 0));
            }

            if (command == "checkout")
            {
                // "checkout HEAD -- <paths>" is an unstage+restore operation.
                // TODO: investigate whether "checkout <non-HEAD-tree> -- <paths>" also
                // needs PrepareForUnstage protection. It re-stages files (sets index to
                // a different tree-ish) and could hit the same skip-worktree interference
                // if the target files were staged by cherry-pick -n / merge and aren't in
                // ModifiedPaths. Currently scoped to HEAD only as the common unstage case.
                bool hasHead = args.Any(arg => arg.Equals("HEAD", StringComparison.OrdinalIgnoreCase));
                bool hasDashDash = args.Any(arg => arg == "--");
                return hasHead && hasDashDash;
            }

            return false;
        }

        /// <summary>
        /// Extracts pathspec arguments from a restore/checkout unstage command.
        /// Returns a <see cref="PathspecResult"/> containing either inline pathspecs,
        /// a --pathspec-from-file reference, or a failure indicator.
        ///
        /// When --pathspec-from-file is specified, the file path is returned so the
        /// caller can forward it through IPC to the mount process, which passes it
        /// to git diff --cached --pathspec-from-file.
        /// </summary>
        public static PathspecResult GetRestorePathspec(string command, string[] args)
        {
            // args[0] = hook type, args[1] = git command, rest are arguments
            List<string> paths = new List<string>();
            bool pastDashDash = false;
            bool skipNext = false;
            bool isCheckout = command == "checkout";

            // For checkout, the first non-option arg before -- is the tree-ish (e.g. HEAD),
            // not a pathspec. Track whether we've consumed it.
            bool treeishConsumed = false;

            // --pathspec-from-file support: collect the file path and nul flag
            string pathspecFromFile = null;
            bool pathspecFileNul = false;
            bool captureNextAsPathspecFile = false;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if (captureNextAsPathspecFile)
                {
                    pathspecFromFile = arg;
                    captureNextAsPathspecFile = false;
                    continue;
                }

                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (arg.StartsWith("--git-pid="))
                    continue;

                // Capture --pathspec-from-file value
                if (arg.StartsWith("--pathspec-from-file="))
                {
                    pathspecFromFile = arg.Substring("--pathspec-from-file=".Length);
                    continue;
                }

                if (arg == "--pathspec-from-file")
                {
                    captureNextAsPathspecFile = true;
                    continue;
                }

                if (arg == "--pathspec-file-nul")
                {
                    pathspecFileNul = true;
                    continue;
                }

                if (arg == "--")
                {
                    pastDashDash = true;
                    continue;
                }

                if (!pastDashDash && arg.StartsWith("-"))
                {
                    // For restore: --source and -s take a following argument
                    if (!isCheckout &&
                        (arg == "--source" || arg == "-s"))
                    {
                        skipNext = true;
                    }

                    continue;
                }

                // For checkout, the first positional arg before -- is the tree-ish
                if (isCheckout && !pastDashDash && !treeishConsumed)
                {
                    treeishConsumed = true;
                    continue;
                }

                paths.Add(arg);
            }

            // stdin ("-") is not supported in hook context — the hook's stdin
            // is not connected to the user's terminal
            if (pathspecFromFile == "-")
            {
                return new PathspecResult { Failed = true };
            }

            return new PathspecResult
            {
                InlinePathspecs = paths.Count > 0 ? string.Join("\0", paths) : "",
                PathspecFromFile = pathspecFromFile,
                PathspecFileNul = pathspecFileNul,
            };
        }
    }
}
