using GVFS.Common.NamedPipes;
using System;

namespace GVFS.Hooks
{
    /// <summary>
    /// Partial class for reset-related pre-command handling.
    /// Detects "git reset --mixed" operations and sends a PrepareForReset
    /// message to the GVFS mount process so it can add files that differ
    /// between HEAD and the target commit to ModifiedPaths before git
    /// clears skip-worktree.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Sends a PrepareForReset message to the GVFS mount process, which will
        /// diff HEAD against the target commit and add changed files to ModifiedPaths
        /// so that git will clear skip-worktree and process them during the reset.
        /// </summary>
        private static void SendPrepareForResetMessage(string[] args)
        {
            ResetCommandParser.ResetParseResult parseResult = ResetCommandParser.Parse(args);

            if (!parseResult.IsMixedReset)
            {
                return;
            }

            // Message body is the target commit (or empty for HEAD).
            // For path-based resets (git reset HEAD -- path), we still send the
            // target to let the mount process diff the right trees.
            string body = parseResult.TargetCommit ?? string.Empty;

            string message = string.IsNullOrEmpty(body)
                ? NamedPipeMessages.PrepareForReset.Request
                : NamedPipeMessages.PrepareForReset.Request + "|" + body;

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
                        if (rawResponse != null && rawResponse.StartsWith(NamedPipeMessages.PrepareForReset.SuccessResult))
                        {
                            succeeded = true;
                        }
                        else
                        {
                            failureMessage = "GVFS mount process returned failure for PrepareForReset.";
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
                    "The reset operation cannot safely proceed because GVFS was unable to",
                    "prepare the index entries. This could lead to stale index state where",
                    "skip-worktree files retain incorrect blob SHAs after the reset.",
                    "",
                    "To resolve:",
                    "  1. Run 'gvfs unmount' and 'gvfs mount' to reset the GVFS state",
                    "  2. Retry the reset command");
            }
        }
    }
}
