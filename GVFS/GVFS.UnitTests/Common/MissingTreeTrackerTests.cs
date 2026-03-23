using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class MissingTreeTrackerTests
    {
        private static MissingTreeTracker CreateTracker(int treeCapacity)
        {
            return new MissingTreeTracker(new MockTracer(), treeCapacity);
        }

        // -------------------------------------------------------------------------
        // AddMissingRootTree
        // -------------------------------------------------------------------------

        [TestCase]
        public void AddMissingRootTree_SingleTreeAndCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");

            tracker.TryGetCommits("tree1", out string[] commits).ShouldEqual(true);
            commits.Length.ShouldEqual(1);
            commits[0].ShouldEqual("commit1");
            tracker.GetHighestMissingTreeCount(commits, out _).ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingRootTree_MultipleTreesForSameCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree3", "commit1");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);

            tracker.TryGetCommits("tree1", out string[] c1).ShouldEqual(true);
            c1[0].ShouldEqual("commit1");

            tracker.TryGetCommits("tree2", out string[] c2).ShouldEqual(true);
            c2[0].ShouldEqual("commit1");

            tracker.TryGetCommits("tree3", out string[] c3).ShouldEqual(true);
            c3[0].ShouldEqual("commit1");
        }

        [TestCase]
        public void AddMissingRootTree_SameTreeAddedTwiceToSameCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree1", "commit1");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingRootTree_SameTreeAddedToMultipleCommits()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree1", "commit2");

            // tree1 is now tracked under both commits
            tracker.TryGetCommits("tree1", out string[] commits).ShouldEqual(true);
            commits.Length.ShouldEqual(2);

            // Both commits each have 1 tree
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingRootTree_MultipleTrees_ChecksCount()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);

            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(2);

            tracker.AddMissingRootTree("tree3", "commit1");
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);

            tracker.AddMissingRootTree("tree4", "commit1");
            tracker.AddMissingRootTree("tree5", "commit1");
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(5);
        }

        // -------------------------------------------------------------------------
        // AddMissingSubTrees
        // -------------------------------------------------------------------------

        [TestCase]
        public void AddMissingSubTrees_AddsSubTreesUnderParentsCommits()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("rootTree", "commit1");
            tracker.AddMissingSubTrees("rootTree", new[] { "sub1", "sub2" });

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);

            tracker.TryGetCommits("sub1", out string[] c1).ShouldEqual(true);
            c1[0].ShouldEqual("commit1");

            tracker.TryGetCommits("sub2", out string[] c2).ShouldEqual(true);
            c2[0].ShouldEqual("commit1");
        }

        [TestCase]
        public void AddMissingSubTrees_PropagatesAcrossAllSharingCommits()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            // Two commits share the same root tree
            tracker.AddMissingRootTree("rootTree", "commit1");
            tracker.AddMissingRootTree("rootTree", "commit2");

            tracker.AddMissingSubTrees("rootTree", new[] { "sub1" });

            // sub1 should be tracked under both commits
            tracker.TryGetCommits("sub1", out string[] commits).ShouldEqual(true);
            commits.Length.ShouldEqual(2);

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(2);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(2);
        }

        [TestCase]
        public void AddMissingSubTrees_NoOp_WhenParentNotTracked()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            // Should not throw; parent is not tracked
            tracker.AddMissingSubTrees("unknownParent", new[] { "sub1" });

            tracker.TryGetCommits("sub1", out _).ShouldEqual(false);
        }

        [TestCase]
        public void AddMissingSubTrees_SkipsCommitEvictedDuringLoop()
        {
            // treeCapacity = 2: rootTree fills slot 1, rootTree2 fills slot 2.
            // commit1 and commit2 both share rootTree (1 unique tree so far).
            // commit3 holds rootTree2 (2 unique trees, at capacity).
            // AddMissingSubTrees(rootTree, [sub1]) must add sub1 to commit1 then commit2.
            // Adding sub1 for commit1 fills the 3rd slot, which evicts the LRU commit.
            // commit2 is LRU (added to the tracker last among commit1/commit2 and then not used
            // again, while commit1 just got used), so it is evicted before we process commit2.
            // The loop must skip commit2 rather than crashing.
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 2);

            tracker.AddMissingRootTree("rootTree", "commit1");
            tracker.AddMissingRootTree("rootTree", "commit2");
            tracker.AddMissingRootTree("rootTree2", "commit3");

            // Does not throw, and sub1 ends up under whichever commit survived eviction
            tracker.AddMissingSubTrees("rootTree", new[] { "sub1" });

            // Exactly one of commit1/commit2 was evicted; sub1 exists under the survivor
            bool commit1HasSub1 = tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _) == 2;
            bool commit2HasSub1 = tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _) == 2;
            (commit1HasSub1 || commit2HasSub1).ShouldEqual(true);
            (commit1HasSub1 && commit2HasSub1).ShouldEqual(false);
        }

        [TestCase]
        public void AddMissingSubTrees_DoesNotEvictIfOnlyOneCommit()
        {
            /* This shouldn't be possible if user has a proper threshold and is marking commits
             * as completed, but test to be safe. */
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 2);
            tracker.AddMissingRootTree("rootTree", "commit1");
            tracker.AddMissingSubTrees("rootTree", new[] { "sub1" });
            tracker.AddMissingSubTrees("rootTree", new[] { "sub2" });
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);
        }

        // -------------------------------------------------------------------------
        // TryGetCommits
        // -------------------------------------------------------------------------

        [TestCase]
        public void TryGetCommits_NonExistentTree()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.TryGetCommits("nonexistent", out string[] commits).ShouldEqual(false);
            commits.ShouldBeNull();
        }

        [TestCase]
        public void TryGetCommits_MarksAllCommitsAsRecentlyUsed()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            tracker.AddMissingRootTree("sharedTree", "commit1");
            tracker.AddMissingRootTree("sharedTree", "commit2");
            tracker.AddMissingRootTree("tree2", "commit3");
            tracker.AddMissingRootTree("tree3", "commit4");

            // Access commit1 and commit2 via TryGetCommits
            tracker.TryGetCommits("sharedTree", out _);

            // Adding a fourth tree should evict commit3 (oldest unused)
            tracker.AddMissingRootTree("tree4", "commit5");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit4" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit5" }, out _).ShouldEqual(1);
        }

        // -------------------------------------------------------------------------
        // GetHighestMissingTreeCount
        // -------------------------------------------------------------------------

        [TestCase]
        public void GetHighestMissingTreeCount_NonExistentCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.GetHighestMissingTreeCount(new[] { "nonexistent" }, out string highest).ShouldEqual(0);
            highest.ShouldBeNull();
        }

        [TestCase]
        public void GetHighestMissingTreeCount_ReturnsCommitWithMostTrees()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree3", "commit2");

            int count = tracker.GetHighestMissingTreeCount(new[] { "commit1", "commit2" }, out string highest);
            count.ShouldEqual(2);
            highest.ShouldEqual("commit1");
        }

        [TestCase]
        public void GetHighestMissingTreeCount_DoesNotUpdateLru()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit2");
            tracker.AddMissingRootTree("tree3", "commit3");

            // Query commit1's count (should not update LRU)
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _);

            // Adding a fourth commit should still evict commit1 (oldest)
            tracker.AddMissingRootTree("tree4", "commit4");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit4" }, out _).ShouldEqual(1);
        }

        // -------------------------------------------------------------------------
        // MarkCommitComplete (cascade removal)
        // -------------------------------------------------------------------------

        [TestCase]
        public void MarkCommitComplete_RemovesAllTreesForCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree3", "commit1");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);

            tracker.MarkCommitComplete("commit1");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree2", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree3", out _).ShouldEqual(false);
        }

        [TestCase]
        public void MarkCommitComplete_NonExistentCommit()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            // Should not throw
            tracker.MarkCommitComplete("nonexistent");
        }

        [TestCase]
        public void MarkCommitComplete_CascadesSharedTreesToOtherCommits()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            // commit1 and commit2 share tree1; commit2 also has tree2
            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree1", "commit2");
            tracker.AddMissingRootTree("tree2", "commit2");

            tracker.MarkCommitComplete("commit1");

            // tree1 was in commit1, so it should be removed from commit2 as well
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);

            // tree2 is unrelated to commit1, so commit2 still has it
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.TryGetCommits("tree2", out string[] c2).ShouldEqual(true);
            c2[0].ShouldEqual("commit2");
        }

        [TestCase]
        public void MarkCommitComplete_RemovesOtherCommitWhenItBecomesEmpty()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            // commit2's only tree is shared with commit1
            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree1", "commit2");

            tracker.MarkCommitComplete("commit1");

            // commit2 had only tree1, which was cascaded away, so commit2 should be gone too
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(0);
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);
        }

        [TestCase]
        public void MarkCommitComplete_DoesNotAffectUnrelatedCommits()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 10);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit2");

            tracker.MarkCommitComplete("commit1");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.TryGetCommits("tree2", out string[] c).ShouldEqual(true);
            c[0].ShouldEqual("commit2");
        }

        // -------------------------------------------------------------------------
        // LRU eviction (no cascade)
        // -------------------------------------------------------------------------

        [TestCase]
        public void LruEviction_EvictsOldestCommit()
        {
            // treeCapacity = 3 trees; one tree per commit
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit2");
            tracker.AddMissingRootTree("tree3", "commit3");

            // Adding a fourth tree exceeds treeCapacity, so commit1 (LRU) is evicted
            tracker.AddMissingRootTree("tree4", "commit4");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);

            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit4" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_DoesNotCascadeSharedTreesToOtherCommits()
        {
            // treeCapacity = 3 trees; tree1 is shared so only 2 unique trees + tree3 = 3 total
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            // tree1 is shared between commit1 and commit2 (counts as 1 unique tree)
            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree1", "commit2");
            tracker.AddMissingRootTree("tree3", "commit3");

            // tree4 is the 4th unique tree, exceeding treeCapacity; evicts commit1 (LRU)
            // which removes tree2, freeing up capacity.
            tracker.AddMissingRootTree("tree4", "commit4");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);

            // tree1 is still missing (not yet downloaded), so commit2 retains it
            tracker.TryGetCommits("tree1", out string[] commits).ShouldEqual(true);
            commits.Length.ShouldEqual(1);
            commits[0].ShouldEqual("commit2");
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_AddingTreeToExistingCommitUpdatesLru()
        {
            // treeCapacity = 4 trees; tree1, tree2, tree3 fill it, then tree1b re-uses commit1
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 4);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit2");
            tracker.AddMissingRootTree("tree3", "commit3");

            // Adding tree1b to commit1 marks commit1 as recently used (it's a new unique tree)
            tracker.AddMissingRootTree("tree1b", "commit1");

            // tree4 is the 5th unique tree, exceeding treeCapacity; commit2 is now LRU
            tracker.AddMissingRootTree("tree4", "commit4");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(2);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit4" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_MultipleTreesPerCommit_EvictsEntireCommit()
        {
            // treeCapacity = 4 trees; commit1 holds 3, commit2 holds 1
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 4);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree3", "commit1");
            tracker.AddMissingRootTree("tree4", "commit2");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(3);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);

            // tree5 is the 5th unique tree; evict LRU (commit1) freeing 3 slots, then add tree5
            tracker.AddMissingRootTree("tree5", "commit3");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree2", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree3", out _).ShouldEqual(false);

            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_CapacityOne()
        {
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 1);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);

            tracker.AddMissingRootTree("tree2", "commit2");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_ManyTreesOneCommit_ExceedsCapacity()
        {
            // treeCapacity = 3 trees; all trees belong to commit1
            // Adding a 4th tree must evict commit1 (the only commit) to make room
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit1");
            tracker.AddMissingRootTree("tree3", "commit1");

            // tree4 exceeds the tree treeCapacity; the LRU commit (commit1) is evicted
            // and then commit2 with tree4 is added fresh
            tracker.AddMissingRootTree("tree4", "commit2");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(0);
            tracker.TryGetCommits("tree1", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree2", out _).ShouldEqual(false);
            tracker.TryGetCommits("tree3", out _).ShouldEqual(false);

            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_TryGetCommitsUpdatesLru()
        {
            // treeCapacity = 3 trees, one per commit
            MissingTreeTracker tracker = CreateTracker(treeCapacity: 3);

            tracker.AddMissingRootTree("tree1", "commit1");
            tracker.AddMissingRootTree("tree2", "commit2");
            tracker.AddMissingRootTree("tree3", "commit3");

            // Access commit1 via TryGetCommits (marks it as recently used)
            tracker.TryGetCommits("tree1", out _);

            // tree4 exceeds treeCapacity; commit2 is now LRU
            tracker.AddMissingRootTree("tree4", "commit4");

            tracker.GetHighestMissingTreeCount(new[] { "commit1" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit2" }, out _).ShouldEqual(0);
            tracker.GetHighestMissingTreeCount(new[] { "commit3" }, out _).ShouldEqual(1);
            tracker.GetHighestMissingTreeCount(new[] { "commit4" }, out _).ShouldEqual(1);
        }
    }
}
