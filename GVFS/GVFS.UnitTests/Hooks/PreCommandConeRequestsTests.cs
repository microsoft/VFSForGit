using GVFS.Hooks;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class PreCommandConeRequestsTests
    {
        private List<string> calls;
        private bool pending;

        [SetUp]
        public void SetUp()
        {
            this.calls = new List<string>();
            this.pending = false;
        }

        [TestCase]
        public void UnstageWidensBeforeItPrepares()
        {
            // Git does not match a pathspec with a path inside a sparse-directory entry, so the
            // staged-file query of the preparation must run after the widen.
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.Applied).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?");
        }

        [TestCase("Applied")]
        [TestCase("NoPathsNamed")]
        [TestCase("NoResolvablePaths")]
        [TestCase("TooManyPaths")]
        [TestCase("FeatureDisabled")]
        public void UnstageAppliesAChangeThatThePreparationMadePending(string widenOutcome)
        {
            this.Send(ConeCommandKind.Unstage, widenOutcome: Parse(widenOutcome), preparationMakesPending: true).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?", "apply");
        }

        [TestCase]
        public void UnstageAppliesOnceAChangeThatWasPendingBeforeAWidenThatDidNotApply()
        {
            this.pending = true;
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.NoPathsNamed).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?", "apply");
        }

        [TestCase]
        public void UnstageDoesNotApplyWhenNothingIsPendingAfterThePreparation()
        {
            this.pending = true;
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.Applied, widenClearsPending: true).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?");
        }

        [TestCase("NothingPending")]
        [TestCase("FeatureDisabled")]
        public void UnstageRunsWhenTheApplyHasNothingToDo(string applyOutcome)
        {
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.NoPathsNamed, preparationMakesPending: true, applyOutcome: Parse(applyOutcome))
                .ShouldBeNull();
        }

        [TestCase("TimedOut")]
        [TestCase("Rejected")]
        [TestCase("Failed")]
        [TestCase("MountUnavailable")]
        [TestCase("NotAnIndexReader")]
        public void UnstageStopsWhenTheApplyFails(string applyOutcome)
        {
            // Without the apply, the unstaged file stays inside a sparse-directory entry with
            // skip-worktree set, and Git does not report the change.
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.NoPathsNamed, preparationMakesPending: true, applyOutcome: Parse(applyOutcome))
                .ShouldEqual("apply: " + applyOutcome);
        }

        [TestCase("TimedOut")]
        [TestCase("ReplyTimedOut")]
        [TestCase("Rejected")]
        [TestCase("Failed")]
        [TestCase("MountUnavailable")]
        [TestCase("NotLockHolder")]
        public void UnstageStopsWithoutPreparingWhenTheWidenIsNotApplied(string widenOutcome)
        {
            // An unstage writes the index, so it stops for every failed widen, as a command that
            // writes the index does. The preparation does not run.
            this.Send(ConeCommandKind.Unstage, widenOutcome: Parse(widenOutcome)).ShouldEqual("widen: " + widenOutcome);

            this.calls.ShouldMatchInOrder("pending?", "widen", "managed?");
        }

        [TestCase]
        public void UnstageRunsWhenGVFSDoesNotManageTheCone()
        {
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.Rejected, isConeManaged: false).ShouldBeNull();
            this.Send(ConeCommandKind.Unstage, widenOutcome: ConeRequestOutcome.NoPathsNamed, preparationMakesPending: true, applyOutcome: ConeRequestOutcome.Rejected, isConeManaged: false)
                .ShouldBeNull();
        }

        [TestCase]
        public void OtherCommandSkipsTheApplyAfterAnAppliedWiden()
        {
            this.pending = true;
            this.Send(ConeCommandKind.Other, widenOutcome: ConeRequestOutcome.Applied).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase("Failed")]
        [TestCase("Rejected")]
        [TestCase("ReplyTimedOut")]
        [TestCase("LockUnavailable")]
        [TestCase("MountUnavailable")]
        public void OtherCommandRunsAndReportsAFailedApplyForTheWarning(string applyOutcome)
        {
            // The command runs, but it can miss the pending change, so the hook warns.
            this.pending = true;
            this.Send(ConeCommandKind.Other, widenOutcome: ConeRequestOutcome.Rejected, applyOutcome: Parse(applyOutcome))
                .ShouldEqual("apply: " + applyOutcome);

            this.calls.ShouldMatchInOrder("pending?", "widen", "apply");
        }

        [TestCase("Applied")]
        [TestCase("NothingPending")]
        [TestCase("FeatureDisabled")]
        [TestCase("NotAnIndexReader")]
        public void OtherCommandHasNoWarningWhenTheApplyLeavesNoGapOrIsNotNeeded(string applyOutcome)
        {
            this.pending = true;
            this.Send(ConeCommandKind.Other, widenOutcome: ConeRequestOutcome.NotLockHolder, applyOutcome: Parse(applyOutcome)).ShouldBeNull();
        }

        [TestCase]
        public void OtherCommandDoesNotApplyWhenNothingIsPending()
        {
            this.Send(ConeCommandKind.Other, widenOutcome: ConeRequestOutcome.NoPathsNamed).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase]
        public void OtherCommandHasNoWarningForAFailedWiden()
        {
            // The mount refuses every widen of a command that does not hold the GVFS lock.
            this.Send(ConeCommandKind.Other, widenOutcome: ConeRequestOutcome.NotLockHolder).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase("Rejected")]
        [TestCase("TimedOut")]
        [TestCase("ReplyTimedOut")]
        [TestCase("MountUnavailable")]
        [TestCase("Failed")]
        [TestCase("NotLockHolder")]
        public void IndexWriterStopsWhenTheWidenIsNotApplied(string widenOutcome)
        {
            // Git has no index entry for the named path, so it fails or skips the path.
            this.pending = true;
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: Parse(widenOutcome)).ShouldEqual("widen: " + widenOutcome);

            this.calls.ShouldMatchInOrder("pending?", "widen", "managed?");
        }

        [TestCase("Applied")]
        [TestCase("NoPathsNamed")]
        [TestCase("NoConeFile")]
        [TestCase("FeatureDisabled")]
        [TestCase("SkippedStash")]
        [TestCase("SkippedInCone")]
        [TestCase("NoResolvablePaths")]
        [TestCase("TooManyPaths")]
        public void IndexWriterRunsWhenTheWidenIsDoneOrNotNeeded(string widenOutcome)
        {
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: Parse(widenOutcome)).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase("Rejected")]
        [TestCase("ReplyTimedOut")]
        [TestCase("MountUnavailable")]
        [TestCase("Failed")]
        [TestCase("NotAnIndexReader")]
        [TestCase("LockUnavailable")]
        [TestCase("NotLockHolder")]
        public void IndexWriterStopsWhenThePendingApplyFails(string applyOutcome)
        {
            // Git has no index entry for the changed file, so it does not see the change.
            this.pending = true;
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: ConeRequestOutcome.NoPathsNamed, applyOutcome: Parse(applyOutcome))
                .ShouldEqual("apply: " + applyOutcome);

            this.calls.ShouldMatchInOrder("pending?", "widen", "apply", "managed?");
        }

        [TestCase("Applied")]
        [TestCase("NothingPending")]
        [TestCase("FeatureDisabled")]
        public void IndexWriterRunsWhenTheApplyLeavesNothingPending(string applyOutcome)
        {
            this.pending = true;
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: ConeRequestOutcome.SkippedInCone, applyOutcome: Parse(applyOutcome)).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "apply");
        }

        [TestCase]
        public void IndexWriterSkipsTheApplyAfterAnAppliedWiden()
        {
            this.pending = true;
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: ConeRequestOutcome.Applied, applyOutcome: ConeRequestOutcome.Rejected).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase]
        public void IndexWriterRunsWhenGVFSDoesNotManageTheCone()
        {
            this.pending = true;
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: ConeRequestOutcome.Failed, isConeManaged: false).ShouldBeNull();
            this.Send(ConeCommandKind.WritesIndex, widenOutcome: ConeRequestOutcome.NoPathsNamed, applyOutcome: ConeRequestOutcome.Rejected, isConeManaged: false)
                .ShouldBeNull();
        }

        [TestCase]
        public void StopMessageNamesTheRequestAndTheOutcome()
        {
            new ConeRequestFailure(ConeRequestFailure.Apply, ConeRequestOutcome.ReplyTimedOut).GetIndexWriteStoppedMessage().ShouldMatchInOrder(
                "GVFS could not prepare the sparse index (apply: ReplyTimedOut). Run the command again.",
                "If it fails again, run 'gvfs status' to check the mount, or run 'gvfs unmount' and 'gvfs mount'.");
        }

        [TestCase("NotLockHolder")]
        [TestCase("LockUnavailable")]
        public void StopMessageNamesTheGVFSLock(string outcome)
        {
            new ConeRequestFailure(ConeRequestFailure.Widen, Parse(outcome)).GetIndexWriteStoppedMessage().ShouldMatchInOrder(
                $"GVFS could not prepare the sparse index (widen: {outcome}).",
                "This Git command does not hold the GVFS lock. Usually another Git command holds it.",
                "Run the command again when the other Git command is done.");
        }

        [TestCase]
        public void WarningNamesTheRequestAndTheOutcome()
        {
            new ConeRequestFailure(ConeRequestFailure.Apply, ConeRequestOutcome.Rejected).GetStaleIndexWarning().ShouldEqual(
                "GVFS: the sparse index is not up to date (apply: Rejected); this command can miss changes outside the sparse-checkout cone. Run it again.");
        }

        // ConeRequestOutcome is internal, and NUnit test methods are public, so test cases name it.
        private static ConeRequestOutcome Parse(string outcome)
        {
            return Enum.Parse<ConeRequestOutcome>(outcome);
        }

        private string Send(
            ConeCommandKind kind,
            ConeRequestOutcome widenOutcome,
            bool preparationMakesPending = false,
            bool widenClearsPending = false,
            ConeRequestOutcome applyOutcome = ConeRequestOutcome.Applied,
            bool isConeManaged = true)
        {
            return PreCommandConeRequests.Send(
                kind,
                () =>
                {
                    this.calls.Add("pending?");
                    return this.pending;
                },
                () =>
                {
                    this.calls.Add("widen");
                    if (widenClearsPending)
                    {
                        this.pending = false;
                    }

                    return widenOutcome;
                },
                () =>
                {
                    this.calls.Add("prepare");
                    if (preparationMakesPending)
                    {
                        this.pending = true;
                    }
                },
                () =>
                {
                    this.calls.Add("apply");
                    return applyOutcome;
                },
                () =>
                {
                    this.calls.Add("managed?");
                    return isConeManaged;
                })?.ToString();
        }
    }
}
