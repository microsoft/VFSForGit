using System;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Windows
{
    [TestFixture]
    public class EnumerationFailureTrackerTests
    {
        // Retention/interval used by the classification and dedup tests, where entries must survive
        // for the duration of the test (the default 30s throttle keeps the auto-prune from firing).
        private static EnumerationFailureTracker CreateTracker()
        {
            return new EnumerationFailureTracker();
        }

        // Retention set to already-expired so a forced prune reclaims every entry deterministically,
        // without any Thread.Sleep. The interval is left at a normal value; PruneForTest bypasses it.
        private static EnumerationFailureTracker CreateImmediatelyExpiringTracker()
        {
            return new EnumerationFailureTracker(
                recentlyEndedRetention: TimeSpan.FromMilliseconds(-1),
                reportedMissingRetention: TimeSpan.FromMilliseconds(-1),
                recentlyEvictedRetention: TimeSpan.FromMilliseconds(-1),
                pruneInterval: TimeSpan.FromSeconds(30));
        }

        [TestCase]
        public void ClassifyMiss_UnknownIdIsNeverSeen()
        {
            EnumerationFailureTracker tracker = CreateTracker();

            tracker.ClassifyMiss(Guid.NewGuid()).ShouldEqual(EnumerationFailureReason.NeverSeen);
        }

        [TestCase]
        public void ClassifyMiss_RecordedEndIsEndedRecently()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            tracker.RecordEnded(id);

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.EndedRecently);
        }

        [TestCase]
        public void ClassifyMiss_RecordedEvictionIsEvicted()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            tracker.RecordEvicted(id);

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.Evicted);
        }

        [TestCase]
        public void ClassifyMiss_EvictionWinsOverRecordedEnd()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            // Same ID present as both evicted and ended (the real case: eviction removed it, then a
            // late End recorded it). Eviction is the more actionable cause and must win.
            tracker.RecordEvicted(id);
            tracker.RecordEnded(id);

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.Evicted);
        }

        [TestCase]
        public void UndoEvicted_UndoesEviction()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            // Eviction recorded the ID before removing it from the active set, then lost the race, so
            // it undoes the record. The ID must no longer be attributed to eviction.
            tracker.RecordEvicted(id);
            tracker.UndoEvicted(id);

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.NeverSeen);
        }

        [TestCase]
        public void UndoEnded_ReclassifiesAsNeverSeen()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            // End recorded the ID before removing it, but the removal found nothing (GVFS never held
            // it), so it undoes the record. The ID must classify NeverSeen, not EndedRecently.
            tracker.RecordEnded(id);
            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.EndedRecently);

            tracker.UndoEnded(id);
            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.NeverSeen);
        }

        [TestCase]
        public void IsRecentlyEvicted_TrueOnlyAfterRecordEvicted()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            tracker.IsRecentlyEvicted(id).ShouldBeFalse();
            tracker.RecordEvicted(id);
            tracker.IsRecentlyEvicted(id).ShouldBeTrue();
            tracker.UndoEvicted(id);
            tracker.IsRecentlyEvicted(id).ShouldBeFalse();
        }

        [TestCase]
        public void TryReserveReport_ReturnsTrueOnceThenFalseForSameId()
        {
            EnumerationFailureTracker tracker = CreateTracker();
            Guid id = Guid.NewGuid();

            tracker.TryReserveReport(id).ShouldBeTrue();
            tracker.TryReserveReport(id).ShouldBeFalse();
            tracker.TryReserveReport(id).ShouldBeFalse();
        }

        [TestCase]
        public void TryReserveReport_IndependentPerId()
        {
            EnumerationFailureTracker tracker = CreateTracker();

            tracker.TryReserveReport(Guid.NewGuid()).ShouldBeTrue();
            tracker.TryReserveReport(Guid.NewGuid()).ShouldBeTrue();
        }

        [TestCase]
        public void PruneRemovesAgedEntries()
        {
            EnumerationFailureTracker tracker = CreateImmediatelyExpiringTracker();
            Guid id = Guid.NewGuid();

            // Populate the maps. The default-interval throttle keeps the auto-prune inside RecordEnded,
            // RecordEvicted and TryReserveReport from firing yet, so the entries are present.
            tracker.RecordEnded(id);
            tracker.TryReserveReport(id).ShouldBeTrue();
            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.EndedRecently);
            tracker.TryReserveReport(id).ShouldBeFalse();

            // Force the prune past the throttle: both aged entries are reclaimed.
            tracker.PruneForTest();

            // The ended entry is gone (now NeverSeen) and the dedup entry is gone (can report again).
            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.NeverSeen);
            tracker.TryReserveReport(id).ShouldBeTrue();
        }

        [TestCase]
        public void PruneRemovesAgedEviction()
        {
            EnumerationFailureTracker tracker = CreateImmediatelyExpiringTracker();
            Guid id = Guid.NewGuid();

            tracker.RecordEvicted(id);
            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.Evicted);

            tracker.PruneForTest();

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.NeverSeen);
        }

        [TestCase]
        public void PruneKeepsEntriesWithinRetention()
        {
            // Long retention: a forced prune must NOT remove fresh entries.
            EnumerationFailureTracker tracker = new EnumerationFailureTracker(
                recentlyEndedRetention: TimeSpan.FromMinutes(10),
                reportedMissingRetention: TimeSpan.FromMinutes(10),
                recentlyEvictedRetention: TimeSpan.FromMinutes(10),
                pruneInterval: TimeSpan.FromSeconds(30));
            Guid id = Guid.NewGuid();

            tracker.RecordEnded(id);
            tracker.TryReserveReport(id).ShouldBeTrue();

            tracker.PruneForTest();

            tracker.ClassifyMiss(id).ShouldEqual(EnumerationFailureReason.EndedRecently);
            tracker.TryReserveReport(id).ShouldBeFalse();
        }
    }
}
