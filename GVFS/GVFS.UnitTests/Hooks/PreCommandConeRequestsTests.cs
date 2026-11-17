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
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.Applied).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?");
        }

        [TestCase("Applied")]
        [TestCase("NoPathsNamed")]
        [TestCase("NoResolvablePaths")]
        public void UnstageAppliesAChangeThatThePreparationMadePending(string widenOutcome)
        {
            this.Send(isUnstage: true, widenOutcome: Parse(widenOutcome), preparationMakesPending: true).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?", "apply");
        }

        [TestCase]
        public void UnstageAppliesOnceAChangeThatWasPendingBeforeAWidenThatDidNotApply()
        {
            this.pending = true;
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.NoPathsNamed).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?", "apply");
        }

        [TestCase]
        public void UnstageDoesNotApplyWhenNothingIsPendingAfterThePreparation()
        {
            this.pending = true;
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.Applied, widenClearsPending: true).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "prepare", "pending?");
        }

        [TestCase("NothingPending")]
        [TestCase("FeatureDisabled")]
        public void UnstageRunsWhenTheApplyHasNothingToDo(string applyOutcome)
        {
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.NoPathsNamed, preparationMakesPending: true, applyOutcome: Parse(applyOutcome))
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
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.NoPathsNamed, preparationMakesPending: true, applyOutcome: Parse(applyOutcome))
                .ShouldEqual("apply: " + applyOutcome);
        }

        [TestCase]
        public void UnstageStopsWithoutPreparingWhenTheWidenTimedOut()
        {
            // The mount can still change the cone and the index after the hook stops waiting.
            this.Send(isUnstage: true, widenOutcome: ConeRequestOutcome.TimedOut).ShouldEqual("widen: TimedOut");

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase]
        public void OtherCommandSkipsTheApplyAfterAnAppliedWiden()
        {
            this.pending = true;
            this.Send(isUnstage: false, widenOutcome: ConeRequestOutcome.Applied).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        [TestCase]
        public void OtherCommandAppliesAPendingChangeWhenTheWidenDidNotApply()
        {
            this.pending = true;
            this.Send(isUnstage: false, widenOutcome: ConeRequestOutcome.TimedOut, applyOutcome: ConeRequestOutcome.Failed).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen", "apply");
        }

        [TestCase]
        public void OtherCommandDoesNotApplyWhenNothingIsPending()
        {
            this.Send(isUnstage: false, widenOutcome: ConeRequestOutcome.NoPathsNamed).ShouldBeNull();

            this.calls.ShouldMatchInOrder("pending?", "widen");
        }

        // ConeRequestOutcome is internal, and NUnit test methods are public, so test cases name it.
        private static ConeRequestOutcome Parse(string outcome)
        {
            return Enum.Parse<ConeRequestOutcome>(outcome);
        }

        private string Send(
            bool isUnstage,
            ConeRequestOutcome widenOutcome,
            bool preparationMakesPending = false,
            bool widenClearsPending = false,
            ConeRequestOutcome applyOutcome = ConeRequestOutcome.Applied)
        {
            return PreCommandConeRequests.Send(
                isUnstage,
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
                });
        }
    }
}
