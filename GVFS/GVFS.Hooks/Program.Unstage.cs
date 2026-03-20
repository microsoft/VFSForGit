using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Hooks
{
    /// <summary>
    /// Partial class for unstage-related pre-command handling.
    /// Detects "restore --staged" and "checkout HEAD --" operations and sends
    /// a PrepareForUnstage message to the GVFS mount process so it can add
    /// staged files to ModifiedPaths before git clears skip-worktree.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Detects whether the git command is an unstage operation that may need
        /// special handling for VFS projections.
        /// Matches: "restore --staged", "restore -S", "checkout HEAD --"
        /// </summary>
        private static bool IsUnstageOperation(string command, string[] args)
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
                // "checkout HEAD -- <paths>" is an unstage+restore operation
                bool hasHead = args.Any(arg => arg.Equals("HEAD", StringComparison.OrdinalIgnoreCase));
                bool hasDashDash = args.Any(arg => arg == "--");
                return hasHead && hasDashDash;
            }

            return false;
        }

        /// <summary>
        /// Extracts pathspec arguments from a restore --staged command.
        /// Returns null-separated pathspecs, or empty string for all staged files.
        /// </summary>
        private static string GetRestorePathspec(string command, string[] args)
        {
            // args[0] = hook type, args[1] = git command, rest are arguments
            // Skip flags (--staged, -S, --source=, -s, etc.) and extract paths
            List<string> paths = new List<string>();
            bool pastDashDash = false;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("--git-pid="))
                    continue;
                if (arg == "--")
                {
                    pastDashDash = true;
                    continue;
                }
                if (!pastDashDash && arg.StartsWith("-"))
                    continue;

                paths.Add(arg);
            }

            return paths.Count > 0 ? string.Join("\0", paths) : "";
        }

        /// <summary>
        /// Sends a PrepareForUnstage message to the GVFS mount process, which will
        /// add staged files matching the pathspec to ModifiedPaths so that git will
        /// clear skip-worktree and process them.
        /// </summary>
        private static void SendPrepareForUnstageMessage(string command, string[] args)
        {
            string pathspec = GetRestorePathspec(command, args);
            string message = string.IsNullOrEmpty(pathspec)
                ? NamedPipeMessages.PrepareForUnstage.Request
                : NamedPipeMessages.PrepareForUnstage.Request + "|" + pathspec;

            bool succeeded = false;
            string failureMessage = null;

            try
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(enlistmentPipename))
                {
                    if (pipeClient.Connect())
                    {
                        pipeClient.SendRequest(message);
                        string rawResponse = pipeClient.ReadRawResponse();
                        if (rawResponse != null && rawResponse.StartsWith(NamedPipeMessages.PrepareForUnstage.SuccessResult))
                        {
                            succeeded = true;
                        }
                        else
                        {
                            failureMessage = "GVFS mount process returned failure for PrepareForUnstage.";
                        }
                    }
                    else
                    {
                        failureMessage = "Unable to connect to GVFS mount process.";
                    }
                }
            }
            catch (Exception e)
            {
                failureMessage = "Exception communicating with GVFS: " + e.Message;
            }

            if (!succeeded && failureMessage != null)
            {
                ExitWithError(
                    failureMessage,
                    "The unstage operation cannot safely proceed because GVFS was unable to",
                    "prepare the staged files. This could lead to index corruption.",
                    "",
                    "To resolve:",
                    "  1. Run 'gvfs unmount' and 'gvfs mount' to reset the GVFS state",
                    "  2. Retry the restore --staged command",
                    "If the problem persists, run 'gvfs repair' or re-clone the enlistment.");
            }
        }
    }
}
