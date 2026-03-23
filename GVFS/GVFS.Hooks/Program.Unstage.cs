using GVFS.Common.NamedPipes;
using System;

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
        /// Sends a PrepareForUnstage message to the GVFS mount process, which will
        /// add staged files matching the pathspec to ModifiedPaths so that git will
        /// clear skip-worktree and process them.
        /// </summary>
        private static void SendPrepareForUnstageMessage(string command, string[] args)
        {
            UnstageCommandParser.PathspecResult pathspecResult = UnstageCommandParser.GetRestorePathspec(command, args);

            if (pathspecResult.Failed)
            {
                ExitWithError(
                    "VFS for Git was unable to determine the pathspecs for this unstage operation.",
                    "This can happen when --pathspec-from-file=- (stdin) is used.",
                    "",
                    "Instead, pass the paths directly on the command line:",
                    "  git restore --staged <path1> <path2> ...");
                return;
            }

            // Build the message body. Format:
            //   null/empty          → all staged files (no pathspec)
            //   "path1\0path2"      → inline pathspecs (null-separated)
            //   "\nF\n<filepath>"   → --pathspec-from-file (mount forwards to git)
            //   "\nFZ\n<filepath>"  → --pathspec-from-file with --pathspec-file-nul
            // The leading \n distinguishes file-reference bodies from inline pathspecs.
            string body;
            if (pathspecResult.PathspecFromFile != null)
            {
                string prefix = pathspecResult.PathspecFileNul ? "\nFZ\n" : "\nF\n";
                body = prefix + pathspecResult.PathspecFromFile;

                // If there are also inline pathspecs, append them after another \n
                if (!string.IsNullOrEmpty(pathspecResult.InlinePathspecs))
                {
                    body += "\n" + pathspecResult.InlinePathspecs;
                }
            }
            else
            {
                body = pathspecResult.InlinePathspecs;
            }

            string message = string.IsNullOrEmpty(body)
                ? NamedPipeMessages.PrepareForUnstage.Request
                : NamedPipeMessages.PrepareForUnstage.Request + "|" + body;

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
