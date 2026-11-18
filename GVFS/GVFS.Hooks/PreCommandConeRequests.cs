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
    /// For an unstage command, a widen that timed out or an apply that failed can hide the
    /// unstaged change, so the hook must stop the command (see <see cref="Send"/>).
    /// </para>
    /// <para>
    /// For a command that writes the index (see <see cref="ConeCommandKind.WritesIndex"/>), a
    /// widen or an apply that the hook sent and the mount did not apply means that Git has no
    /// index entry for a path or a change that the command needs. Git then fails, or it skips the
    /// change and reports success. Thus the hook stops the command before Git runs. The hook
    /// never stops a command when GVFS does not manage a sparse-index cone.
    /// </para>
    /// <para>
    /// For other commands, a failed request only makes Git slower, so Git always runs.
    /// </para>
    /// <para>
    /// All dependencies are delegates, so that unit tests can check the order without a mount.
    /// </para>
    /// </remarks>
    internal static class PreCommandConeRequests
    {
        /// <param name="kind">The kind of the command (see <see cref="ConeCommandClassifier"/>).</param>
        /// <param name="isConeChangePending">Returns true when the mount's pending file exists.</param>
        /// <param name="sendWiden">Asks the mount to widen the cone for the named paths.</param>
        /// <param name="sendPrepareForUnstage">Asks the mount to add the staged files to the modified paths.</param>
        /// <param name="applyPendingConeChange">Asks the mount to apply the pending cone change.</param>
        /// <param name="isSparseIndexConeManaged">
        /// Returns true when GVFS manages a sparse-index cone. Called only before the hook stops a
        /// command that writes the index.
        /// </param>
        /// <returns>The request that makes the command unsafe, or null when Git can run.</returns>
        public static ConeRequestFailure Send(
            ConeCommandKind kind,
            Func<bool> isConeChangePending,
            Func<ConeRequestOutcome> sendWiden,
            Action sendPrepareForUnstage,
            Func<ConeRequestOutcome> applyPendingConeChange,
            Func<bool> isSparseIndexConeManaged)
        {
            ArgumentNullException.ThrowIfNull(isConeChangePending);
            ArgumentNullException.ThrowIfNull(sendWiden);
            ArgumentNullException.ThrowIfNull(sendPrepareForUnstage);
            ArgumentNullException.ThrowIfNull(applyPendingConeChange);
            ArgumentNullException.ThrowIfNull(isSparseIndexConeManaged);

            // Check before the widen: a widen that the mount applied also applied the pending change.
            bool pending = isConeChangePending();
            ConeRequestOutcome widenOutcome = sendWiden();

            switch (kind)
            {
                case ConeCommandKind.Unstage:
                    return SendForUnstage(widenOutcome, isConeChangePending, sendPrepareForUnstage, applyPendingConeChange);

                case ConeCommandKind.WritesIndex:
                    if (!IsWidenDone(widenOutcome))
                    {
                        return isSparseIndexConeManaged() ? new ConeRequestFailure(ConeRequestFailure.Widen, widenOutcome) : null;
                    }

                    if (pending && widenOutcome != ConeRequestOutcome.Applied)
                    {
                        ConeRequestOutcome applyOutcome = applyPendingConeChange();
                        if (!IsApplyDone(applyOutcome) && isSparseIndexConeManaged())
                        {
                            return new ConeRequestFailure(ConeRequestFailure.Apply, applyOutcome);
                        }
                    }

                    return null;

                default:
                    if (pending && widenOutcome != ConeRequestOutcome.Applied)
                    {
                        applyPendingConeChange();
                    }

                    return null;
            }
        }

        /// <summary>
        /// Return true when the cone includes the named paths after the widen, or when the hook
        /// did not need to send it, or could not use it (for example for paths outside the worktree).
        /// </summary>
        internal static bool IsWidenDone(ConeRequestOutcome outcome)
        {
            switch (outcome)
            {
                case ConeRequestOutcome.Applied:
                case ConeRequestOutcome.NoPathsNamed:
                case ConeRequestOutcome.NoConeFile:
                case ConeRequestOutcome.FeatureDisabled:
                case ConeRequestOutcome.SkippedStash:
                case ConeRequestOutcome.SkippedInCone:
                case ConeRequestOutcome.NoResolvablePaths:
                case ConeRequestOutcome.TooManyPaths:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Return true when no pending change is left after the apply.</summary>
        internal static bool IsApplyDone(ConeRequestOutcome outcome)
        {
            switch (outcome)
            {
                case ConeRequestOutcome.Applied:
                case ConeRequestOutcome.NothingPending:
                case ConeRequestOutcome.FeatureDisabled:
                    return true;

                default:
                    return false;
            }
        }

        private static ConeRequestFailure SendForUnstage(
            ConeRequestOutcome widenOutcome,
            Func<bool> isConeChangePending,
            Action sendPrepareForUnstage,
            Func<ConeRequestOutcome> applyPendingConeChange)
        {
            // After a timeout, the mount can still change the cone and the index. The staged-file
            // query and Git can then see an index that does not have the named paths yet.
            if (widenOutcome == ConeRequestOutcome.TimedOut)
            {
                return new ConeRequestFailure(ConeRequestFailure.Widen, widenOutcome);
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
            return IsApplyDone(applyOutcome) ? null : new ConeRequestFailure(ConeRequestFailure.Apply, applyOutcome);
        }
    }

    /// <summary>A cone request that did not prepare the index for a command.</summary>
    internal sealed class ConeRequestFailure
    {
        public const string Widen = "widen";
        public const string Apply = "apply";

        public ConeRequestFailure(string request, ConeRequestOutcome outcome)
        {
            this.Request = request;
            this.Outcome = outcome;
        }

        /// <summary><see cref="Widen"/> or <see cref="Apply"/>.</summary>
        public string Request { get; }

        public ConeRequestOutcome Outcome { get; }

        /// <summary>True when the request failed because another Git command holds the GVFS lock.</summary>
        public bool IsLockConflict => this.Outcome == ConeRequestOutcome.NotLockHolder || this.Outcome == ConeRequestOutcome.LockUnavailable;

        /// <summary>
        /// Return the message for a command that writes the index, when the hook stops it before
        /// Git runs.
        /// </summary>
        public string[] GetIndexWriteStoppedMessage()
        {
            string first = "GVFS could not prepare the sparse index (" + this + ").";
            return this.IsLockConflict
                ? new[] { first, "Another Git command holds the GVFS lock. Run the command again when that command is done." }
                : new[] { first + " Run the command again." };
        }

        public override string ToString()
        {
            return this.Request + ": " + this.Outcome;
        }
    }
}
