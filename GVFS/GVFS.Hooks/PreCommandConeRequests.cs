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
    /// An unstage command also writes the index. A widen or an apply that the hook sent and the
    /// mount did not apply can hide the unstaged change, so the hook stops the command with the
    /// same rules as for a command that writes the index (see <see cref="Send"/>). A failed
    /// widen stops the command before the preparation.
    /// </para>
    /// <para>
    /// For a command that writes the index (see <see cref="ConeCommandKind.WritesIndex"/>), a
    /// widen or an apply that the hook sent and the mount did not apply means that Git has no
    /// index entry for a path or a change that the command needs. Git then fails, or it skips the
    /// change and reports success. Thus the hook stops the command before Git runs. The hook
    /// never stops a command when it knows that GVFS does not manage a sparse-index cone. When it
    /// cannot read the cone file or the feature flag, it stops the command.
    /// </para>
    /// <para>
    /// Other commands always run. Read-only commands do not lose a change, but after a failed
    /// apply they can miss a working-tree change outside the cone, so the hook warns.
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
        /// <param name="mayManageSparseIndexCone">
        /// Returns false only when it is known that GVFS does not manage a sparse-index cone.
        /// Called only before the hook stops a command that writes the index or unstages files.
        /// </param>
        /// <returns>
        /// The request that did not prepare the index, or null. For a command that writes the
        /// index or unstages files, the hook stops the command. For other commands, Git runs and
        /// the hook warns.
        /// </returns>
        public static ConeRequestFailure Send(
            ConeCommandKind kind,
            Func<bool> isConeChangePending,
            Func<ConeRequestOutcome> sendWiden,
            Action sendPrepareForUnstage,
            Func<ConeRequestOutcome> applyPendingConeChange,
            Func<bool> mayManageSparseIndexCone)
        {
            ArgumentNullException.ThrowIfNull(isConeChangePending);
            ArgumentNullException.ThrowIfNull(sendWiden);
            ArgumentNullException.ThrowIfNull(sendPrepareForUnstage);
            ArgumentNullException.ThrowIfNull(applyPendingConeChange);
            ArgumentNullException.ThrowIfNull(mayManageSparseIndexCone);

            // Check before the widen: a widen that the mount applied also applied the pending change.
            bool pending = isConeChangePending();
            ConeRequestOutcome widenOutcome = sendWiden();

            switch (kind)
            {
                case ConeCommandKind.Unstage:
                    return SendForUnstage(widenOutcome, isConeChangePending, sendPrepareForUnstage, applyPendingConeChange, mayManageSparseIndexCone);

                case ConeCommandKind.WritesIndex:
                    if (!WidenLeavesNoGap(widenOutcome))
                    {
                        return mayManageSparseIndexCone() ? new ConeRequestFailure(ConeRequestFailure.Widen, widenOutcome) : null;
                    }

                    if (pending && widenOutcome != ConeRequestOutcome.Applied)
                    {
                        ConeRequestOutcome applyOutcome = applyPendingConeChange();
                        if (!ApplyLeavesNoGap(applyOutcome) && mayManageSparseIndexCone())
                        {
                            return new ConeRequestFailure(ConeRequestFailure.Apply, applyOutcome);
                        }
                    }

                    return null;

                default:
                    // Git runs. A failed apply can hide a working-tree change from the command, so
                    // the caller warns. A failed widen does not: the mount refuses every widen of a
                    // command that does not hold the GVFS lock, and a named path without a change
                    // is correct after the in-memory expansion.
                    if (pending && widenOutcome != ConeRequestOutcome.Applied)
                    {
                        ConeRequestOutcome applyOutcome = applyPendingConeChange();
                        if (!ApplyLeavesNoGap(applyOutcome) && applyOutcome != ConeRequestOutcome.NotAnIndexReader)
                        {
                            return new ConeRequestFailure(ConeRequestFailure.Apply, applyOutcome);
                        }
                    }

                    return null;
            }
        }

        /// <summary>
        /// Return true when the widen leaves no named path out of the cone that the mount can
        /// add: the mount applied it, the hook did not need it, or no widen can help.
        /// </summary>
        /// <remarks>
        /// <see cref="ConeRequestOutcome.TooManyPaths"/> is here because the mount refuses such a
        /// widen every time, and a stop would stop the command at every attempt. A pending apply
        /// still runs, so Git sees every working-tree change. A named path without a change stays
        /// outside the cone, and Git expands the index in memory: <c>git add</c>, <c>rm</c>, and
        /// <c>mv</c> then fail and report that the path is outside the sparse-checkout
        /// definition.
        /// </remarks>
        internal static bool WidenLeavesNoGap(ConeRequestOutcome outcome)
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
        internal static bool ApplyLeavesNoGap(ConeRequestOutcome outcome)
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
            Func<ConeRequestOutcome> applyPendingConeChange,
            Func<bool> mayManageSparseIndexCone)
        {
            // Without the widen, the staged-file query of the preparation does not find a staged
            // file inside a sparse-directory entry. After a timeout, the mount can also still
            // change the cone and the index.
            if (!WidenLeavesNoGap(widenOutcome))
            {
                return mayManageSparseIndexCone() ? new ConeRequestFailure(ConeRequestFailure.Widen, widenOutcome) : null;
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
            return ApplyLeavesNoGap(applyOutcome) || !mayManageSparseIndexCone()
                ? null
                : new ConeRequestFailure(ConeRequestFailure.Apply, applyOutcome);
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

        /// <summary>
        /// True when the request failed because this Git command does not hold the GVFS lock. The
        /// usual cause is another Git command that holds it.
        /// </summary>
        public bool IsLockConflict => this.Outcome == ConeRequestOutcome.NotLockHolder || this.Outcome == ConeRequestOutcome.LockUnavailable;

        /// <summary>
        /// Return the message for a command that writes the index, when the hook stops it before
        /// Git runs.
        /// </summary>
        public string[] GetIndexWriteStoppedMessage()
        {
            string first = "GVFS could not prepare the sparse index (" + this + ").";
            return this.IsLockConflict
                ? new[]
                {
                    first,
                    "This Git command does not hold the GVFS lock. Usually another Git command holds it.",
                    "Run the command again when the other Git command is done.",
                }
                : new[]
                {
                    first + " Run the command again.",
                    "If it fails again, run 'gvfs status' to check the mount, or run 'gvfs unmount' and 'gvfs mount'.",
                };
        }

        /// <summary>
        /// Return the warning for a command that runs although the apply that it needed did not
        /// succeed.
        /// </summary>
        public string GetStaleIndexWarning()
        {
            return "GVFS: the sparse index is not up to date (" + this + "); this command can miss changes outside the sparse-checkout cone. Run it again.";
        }

        public override string ToString()
        {
            return this.Request + ": " + this.Outcome;
        }
    }
}
