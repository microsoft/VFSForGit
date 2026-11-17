using System;

namespace GVFS.Hooks
{
    /// <summary>
    /// Sends the requests of the pre-command hook that prepare the index before Git reads it:
    /// the cone widen, the unstage preparation, and the apply of a pending cone change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters for an unstage command (for example <c>git restore --staged</c> or
    /// <c>git reset</c>). The mount finds the staged files with <c>git diff --cached</c>. Git
    /// does not match a pathspec with a path inside a sparse-directory entry, so before the
    /// widen that query finds no staged file for a named path outside the cone. Thus the widen
    /// comes first.
    /// </para>
    /// <para>
    /// The unstage preparation adds the staged files to the modified paths. When the cone does
    /// not include one of them (for example for <c>git restore --staged .</c>, which names no
    /// path that the widen can use), the mount makes a cone change pending. The apply then
    /// expands its directory before Git runs. Otherwise Git keeps the file inside a
    /// sparse-directory entry, the file keeps skip-worktree, and Git does not report the
    /// unstaged change.
    /// </para>
    /// <para>
    /// For other commands, a failed request only makes Git slower, so Git always runs. For an
    /// unstage command, a widen that timed out or an apply that failed can hide the unstaged
    /// change, so the hook must stop the command (see <see cref="Send"/>).
    /// </para>
    /// <para>
    /// All dependencies are delegates, so that unit tests can check the order without a mount.
    /// </para>
    /// </remarks>
    internal static class PreCommandConeRequests
    {
        /// <param name="isUnstage">True when the command unstages files.</param>
        /// <param name="isConeChangePending">Returns true when the mount's pending file exists.</param>
        /// <param name="sendWiden">Asks the mount to widen the cone for the named paths.</param>
        /// <param name="sendPrepareForUnstage">Asks the mount to add the staged files to the modified paths.</param>
        /// <param name="applyPendingConeChange">Asks the mount to apply the pending cone change.</param>
        /// <returns>
        /// The request that makes an unstage command unsafe, or null when Git can run. Always
        /// null for other commands.
        /// </returns>
        public static string Send(
            bool isUnstage,
            Func<bool> isConeChangePending,
            Func<ConeRequestOutcome> sendWiden,
            Action sendPrepareForUnstage,
            Func<ConeRequestOutcome> applyPendingConeChange)
        {
            ArgumentNullException.ThrowIfNull(isConeChangePending);
            ArgumentNullException.ThrowIfNull(sendWiden);
            ArgumentNullException.ThrowIfNull(sendPrepareForUnstage);
            ArgumentNullException.ThrowIfNull(applyPendingConeChange);

            // Check before the widen: a widen that the mount applied also applied the pending change.
            bool pending = isConeChangePending();
            ConeRequestOutcome widenOutcome = sendWiden();

            if (!isUnstage)
            {
                if (pending && widenOutcome != ConeRequestOutcome.Applied)
                {
                    applyPendingConeChange();
                }

                return null;
            }

            // After a timeout, the mount can still change the cone and the index. The staged-file
            // query and Git can then see an index that does not have the named paths yet.
            if (widenOutcome == ConeRequestOutcome.TimedOut)
            {
                return "widen: " + widenOutcome;
            }

            sendPrepareForUnstage();

            // The preparation can make a cone change pending. The mount deletes the pending file
            // when it applies a change, so after an applied widen the file exists only for a
            // change that the preparation made.
            if (!isConeChangePending())
            {
                return null;
            }

            ConeRequestOutcome applyOutcome = applyPendingConeChange();
            switch (applyOutcome)
            {
                case ConeRequestOutcome.Applied:
                case ConeRequestOutcome.NothingPending:
                case ConeRequestOutcome.FeatureDisabled:
                    return null;

                default:
                    return "apply: " + applyOutcome;
            }
        }
    }
}
