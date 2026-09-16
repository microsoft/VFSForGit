using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.Platform.Windows
{
    /// <summary>
    /// Why a directory-enumeration Get failed to find its enumeration ID. Recorded on the failure
    /// telemetry so self-inflicted causes can be told apart from ProjFS races outside gvfs.exe's
    /// control. These values are a case-sensitive contract consumed by the release-readiness
    /// telemetry dashboard; keep them in sync with its cause bucketing.
    /// </summary>
    public enum EnumerationFailureReason
    {
        NeverSeen = 0,   // ProjFS delivered an ID GVFS never held: never started, or from before a provider restart (outside gvfs.exe's control).
        Evicted,         // GVFS's own stale-enumeration eviction removed a live enumeration (self-inflicted).
        EndedRecently,   // ProjFS delivered a Get racing or following the End for the same enumeration - a benign close/query race (outside gvfs.exe's control).
    }

    /// <summary>
    /// Tracks the state needed to classify and rate-limit "Failed to find active enumeration ID"
    /// failures, keyed by ProjFS enumeration GUID:
    ///
    /// - Recently ended IDs, so a Get that races or follows the End for the same enumeration is
    ///   attributed to a benign close/query race (<see cref="EnumerationFailureReason.EndedRecently"/>)
    ///   rather than an ID GVFS never held.
    /// - Recently reported IDs, so the error is emitted once per ID within a window instead of once
    ///   per retry when a caller re-enumerates a lost handle in a loop.
    ///
    /// Eviction (a separate concern owned by the virtualizer) is passed in to
    /// <see cref="ClassifyMiss"/> as a flag rather than tracked here.
    ///
    /// Thread-safety: the enumeration callbacks run concurrently on many ProjFS worker threads, so
    /// this deliberately uses lock-free <see cref="ConcurrentDictionary{Guid, Int64}"/> state rather
    /// than a coarse lock, which would serialize the hot enumeration path. GUIDs are never reused, so
    /// entries are bounded purely by age.
    /// </summary>
    public class EnumerationFailureTracker
    {
        // ProjFS can deliver a Get that races the End for the same handle (a query in flight while the
        // directory handle is closing, or the querying process dying mid-enumeration). Ended IDs are
        // retained this long so such a Get is attributed to a recently-ended enumeration.
        private static readonly TimeSpan DefaultRecentlyEndedRetention = TimeSpan.FromSeconds(30);

        // A Get miss for the same ID can repeat in a tight loop (a caller re-enumerating a handle
        // whose Start GVFS lost, e.g. across a provider restart). The error is emitted once per ID
        // within this window so the machine-based signal survives without the per-machine event storm.
        private static readonly TimeSpan DefaultReportedMissingRetention = TimeSpan.FromMinutes(5);

        // GVFS's own stale-enumeration eviction removes a live enumeration that ProjFS never ended.
        // Evicted IDs are retained this long so a later Get for one is attributed to eviction rather
        // than a never-held ID. This is twice the default stale-enumeration timeout (5 minutes), the
        // window the virtualizer's eviction sweep uses to decide an enumeration is stale.
        private static readonly TimeSpan DefaultRecentlyEvictedRetention = TimeSpan.FromMinutes(10);

        // Throttle for the age-based prune. The prune runs from every record point (RecordEnded,
        // RecordEvicted, TryReserveReport), so the maps stay bounded even if one callback (e.g. End)
        // stops arriving.
        private static readonly TimeSpan DefaultPruneInterval = TimeSpan.FromSeconds(30);

        // Key: the ProjFS enumeration GUID that was ended. Value: the Environment.TickCount64
        // (monotonic milliseconds) at which EndDirectoryEnumeration recorded it.
        private readonly ConcurrentDictionary<Guid, long> recentlyEnded = new ConcurrentDictionary<Guid, long>();

        // Key: the ProjFS enumeration GUID for which a miss error was already emitted. Value: the
        // Environment.TickCount64 (monotonic milliseconds) of that first report.
        private readonly ConcurrentDictionary<Guid, long> recentlyReportedMissing = new ConcurrentDictionary<Guid, long>();

        // Key: the ProjFS enumeration GUID that GVFS's stale-enumeration eviction removed. Value: the
        // Environment.TickCount64 (monotonic milliseconds) at which it was evicted.
        private readonly ConcurrentDictionary<Guid, long> recentlyEvicted = new ConcurrentDictionary<Guid, long>();

        private readonly TimeSpan recentlyEndedRetention;
        private readonly TimeSpan reportedMissingRetention;
        private readonly TimeSpan recentlyEvictedRetention;
        private readonly TimeSpan pruneInterval;

        // Monotonic (Environment.TickCount64, milliseconds) timestamp of the last prune.
        private long lastPruneTickCount = Environment.TickCount64;

        public EnumerationFailureTracker()
            : this(DefaultRecentlyEndedRetention, DefaultReportedMissingRetention, DefaultRecentlyEvictedRetention, DefaultPruneInterval)
        {
        }

        public EnumerationFailureTracker(
            TimeSpan recentlyEndedRetention,
            TimeSpan reportedMissingRetention,
            TimeSpan recentlyEvictedRetention,
            TimeSpan pruneInterval)
        {
            this.recentlyEndedRetention = recentlyEndedRetention;
            this.reportedMissingRetention = reportedMissingRetention;
            this.recentlyEvictedRetention = recentlyEvictedRetention;
            this.pruneInterval = pruneInterval;
        }

        /// <summary>
        /// Records that an enumeration has ended. The caller MUST call this before removing the ID from
        /// its active-enumeration collection, so a Get that races the removal always finds the ID in
        /// one collection or the other and is never mis-attributed to a never-held ID.
        /// </summary>
        public void RecordEnded(Guid enumerationId)
        {
            this.MaybePrune();
            this.recentlyEnded[enumerationId] = Environment.TickCount64;
        }

        /// <summary>
        /// Undoes a <see cref="RecordEnded"/> when the End did not actually remove a live enumeration
        /// (GVFS never held the ID), so a later miss is classified <see cref="EnumerationFailureReason.NeverSeen"/>
        /// rather than skewed to <see cref="EnumerationFailureReason.EndedRecently"/>.
        /// </summary>
        public void UndoEnded(Guid enumerationId)
        {
            this.recentlyEnded.TryRemove(enumerationId, out _);
        }

        /// <summary>
        /// Records that GVFS's stale-enumeration eviction removed <paramref name="enumerationId"/>.
        /// The caller MUST call this before removing the ID from its active-enumeration collection so a
        /// racing Get always finds the ID in one collection or the other; if the removal then loses the
        /// race (e.g. a normal End removed it first), call <see cref="UndoEvicted"/> to undo.
        /// </summary>
        public void RecordEvicted(Guid enumerationId)
        {
            this.MaybePrune();
            this.recentlyEvicted[enumerationId] = Environment.TickCount64;
        }

        /// <summary>
        /// Undoes a <see cref="RecordEvicted"/> when the eviction lost the race to remove the ID from
        /// the active collection, so a miss is not mis-attributed to eviction.
        /// </summary>
        public void UndoEvicted(Guid enumerationId)
        {
            this.recentlyEvicted.TryRemove(enumerationId, out _);
        }

        /// <summary>
        /// Whether <paramref name="enumerationId"/> is currently tracked as recently evicted.
        /// </summary>
        public bool IsRecentlyEvicted(Guid enumerationId)
        {
            return this.recentlyEvicted.ContainsKey(enumerationId);
        }

        /// <summary>
        /// Classifies why a Get failed to find <paramref name="enumerationId"/> in the active
        /// collection. Eviction is the most actionable (self-inflicted) cause and wins; otherwise a
        /// recently-ended ID is a benign close/query race, and anything else was never held.
        /// </summary>
        public EnumerationFailureReason ClassifyMiss(Guid enumerationId)
        {
            if (this.recentlyEvicted.ContainsKey(enumerationId))
            {
                return EnumerationFailureReason.Evicted;
            }

            if (this.recentlyEnded.ContainsKey(enumerationId))
            {
                return EnumerationFailureReason.EndedRecently;
            }

            return EnumerationFailureReason.NeverSeen;
        }

        /// <summary>
        /// Reserves the single error report allowed for <paramref name="enumerationId"/> within the
        /// reporting window. Returns true the first time the ID is seen missing and false for repeats,
        /// so a caller's retry loop cannot produce a telemetry storm.
        /// </summary>
        public bool TryReserveReport(Guid enumerationId)
        {
            this.MaybePrune();
            return this.recentlyReportedMissing.TryAdd(enumerationId, Environment.TickCount64);
        }

        // Prunes all three maps if the throttle interval has elapsed. Called from every record point so
        // the maps stay bounded regardless of which callback is active.
        private void MaybePrune()
        {
            if (this.recentlyEnded.IsEmpty && this.recentlyReportedMissing.IsEmpty && this.recentlyEvicted.IsEmpty)
            {
                return;
            }

            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref this.lastPruneTickCount);
            if (now - last < (long)this.pruneInterval.TotalMilliseconds)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref this.lastPruneTickCount, now, last) != last)
            {
                // Another thread just claimed this prune interval.
                return;
            }

            PruneByAge(this.recentlyEnded, now - (long)this.recentlyEndedRetention.TotalMilliseconds);
            PruneByAge(this.recentlyReportedMissing, now - (long)this.reportedMissingRetention.TotalMilliseconds);
            PruneByAge(this.recentlyEvicted, now - (long)this.recentlyEvictedRetention.TotalMilliseconds);
        }

        private static void PruneByAge(ConcurrentDictionary<Guid, long> map, long cutoffTickCount)
        {
            foreach (KeyValuePair<Guid, long> tracked in map)
            {
                if (tracked.Value < cutoffTickCount)
                {
                    map.TryRemove(tracked.Key, out _);
                }
            }
        }

        /// <summary>
        /// Test-only: runs the prune immediately, bypassing the throttle, so retention behavior can be
        /// exercised deterministically.
        /// </summary>
        internal void PruneForTest()
        {
            Interlocked.Exchange(
                ref this.lastPruneTickCount,
                Environment.TickCount64 - (long)this.pruneInterval.TotalMilliseconds - 1);
            this.MaybePrune();
        }
    }
}
