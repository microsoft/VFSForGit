using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class MissingTreeTrackerTests
    {
        [TestCase]
        public void AddMissingTree_SingleTreeAndCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            
            tracker.TryGetCommit("tree1", out string commitSha).ShouldEqual(true);
            commitSha.ShouldEqual("commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingTree_MultipleTreesForSameCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit1");
            tracker.AddMissingTree("tree3", "commit1");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(3);
            
            tracker.TryGetCommit("tree1", out string commit1).ShouldEqual(true);
            commit1.ShouldEqual("commit1");
            
            tracker.TryGetCommit("tree2", out string commit2).ShouldEqual(true);
            commit2.ShouldEqual("commit1");
            
            tracker.TryGetCommit("tree3", out string commit3).ShouldEqual(true);
            commit3.ShouldEqual("commit1");
        }

        [TestCase]
        public void AddMissingTree_SameTreeAddedTwiceToSameCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree1", "commit1");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingTree_TreeReassociatedWithDifferentCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            // Add tree to first commit
            tracker.AddMissingTree("tree1", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
            
            // Reassociate same tree with different commit
            tracker.AddMissingTree("tree1", "commit2");
            
            // Tree should now be associated with commit2
            tracker.TryGetCommit("tree1", out string commitSha).ShouldEqual(true);
            commitSha.ShouldEqual("commit2");
            
            // commit1 should have 0 trees (and be removed)
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            
            // commit2 should have 1 tree
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingTree_TreeReassociatedWithDifferentCommit_OriginalCommitHasOtherTrees()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            // Add multiple trees to first commit
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(2);
            
            // Reassociate tree1 with different commit
            tracker.AddMissingTree("tree1", "commit2");
            
            // Tree1 should now be associated with commit2
            tracker.TryGetCommit("tree1", out string commitSha).ShouldEqual(true);
            commitSha.ShouldEqual("commit2");
            
            // commit1 should still exist with 1 tree
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
            
            // commit2 should have 1 tree
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            
            // tree2 should still be associated with commit1
            tracker.TryGetCommit("tree2", out string tree2Commit).ShouldEqual(true);
            tree2Commit.ShouldEqual("commit1");
        }

        [TestCase]
        public void TryGetCommit_NonExistentTree()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.TryGetCommit("nonexistent", out string commitSha).ShouldEqual(false);
            commitSha.ShouldEqual(null);
        }

        [TestCase]
        public void GetMissingTreeCount_NonExistentCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.GetMissingTreeCount("nonexistent").ShouldEqual(0);
        }

        [TestCase]
        public void RemoveCommit_RemovesAllTrees()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit1");
            tracker.AddMissingTree("tree3", "commit1");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(3);
            
            tracker.RemoveCommit("commit1");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.TryGetCommit("tree1", out string _).ShouldEqual(false);
            tracker.TryGetCommit("tree2", out string _).ShouldEqual(false);
            tracker.TryGetCommit("tree3", out string _).ShouldEqual(false);
        }

        [TestCase]
        public void RemoveCommit_NonExistentCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            // Should not throw
            tracker.RemoveCommit("nonexistent");
        }

        [TestCase]
        public void RemoveCommit_DoesNotAffectOtherCommits()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit2");
            
            tracker.RemoveCommit("commit1");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            tracker.TryGetCommit("tree2", out string commitSha).ShouldEqual(true);
            commitSha.ShouldEqual("commit2");
        }

        [TestCase]
        public void LruEviction_EvictsOldestCommit()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 3);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit2");
            tracker.AddMissingTree("tree3", "commit3");
            
            // All three commits should exist
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
            
            // Adding a fourth commit should evict commit1 (oldest)
            tracker.AddMissingTree("tree4", "commit4");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.TryGetCommit("tree1", out string _).ShouldEqual(false);
            
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit4").ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_AddingTreeToExistingCommitUpdatesLru()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 3);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit2");
            tracker.AddMissingTree("tree3", "commit3");
            
            // Access commit1 by adding another tree to it (marks it as recently used)
            tracker.AddMissingTree("tree1b", "commit1");
            
            // Adding a fourth commit should evict commit2 (now oldest)
            tracker.AddMissingTree("tree4", "commit4");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(2); // Still has tree1 and tree1b
            tracker.GetMissingTreeCount("commit2").ShouldEqual(0); // Evicted
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit4").ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_TryGetCommitUpdatesLru()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 3);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit2");
            tracker.AddMissingTree("tree3", "commit3");
            
            // Access commit1 via TryGetCommit (marks it as recently used)
            tracker.TryGetCommit("tree1", out string _);
            
            // Adding a fourth commit should evict commit2 (now oldest)
            tracker.AddMissingTree("tree4", "commit4");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1); // Still exists
            tracker.GetMissingTreeCount("commit2").ShouldEqual(0); // Evicted
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit4").ShouldEqual(1);
        }

        [TestCase]
        public void LruEviction_EvictsMultipleTrees()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 2);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit1");
            tracker.AddMissingTree("tree3", "commit1");
            
            tracker.AddMissingTree("tree4", "commit2");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(3);
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            
            // Adding a third commit should evict commit1 with all its trees
            tracker.AddMissingTree("tree5", "commit3");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.TryGetCommit("tree1", out string _).ShouldEqual(false);
            tracker.TryGetCommit("tree2", out string _).ShouldEqual(false);
            tracker.TryGetCommit("tree3", out string _).ShouldEqual(false);
            
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
        }

        [TestCase]
        public void Capacity_One()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 1);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
            
            tracker.AddMissingTree("tree2", "commit2");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
        }

        [TestCase]
        public void AddMissingTree_MultipleTrees_ChecksCount()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 10);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(1);
            
            tracker.AddMissingTree("tree2", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(2);
            
            tracker.AddMissingTree("tree3", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(3);
            
            tracker.AddMissingTree("tree4", "commit1");
            tracker.AddMissingTree("tree5", "commit1");
            tracker.GetMissingTreeCount("commit1").ShouldEqual(5);
        }

        [TestCase]
        public void GetMissingTreeCount_DoesNotUpdateLru()
        {
            MissingTreeTracker tracker = new MissingTreeTracker(capacity: 3);
            
            tracker.AddMissingTree("tree1", "commit1");
            tracker.AddMissingTree("tree2", "commit2");
            tracker.AddMissingTree("tree3", "commit3");
            
            // Query commit1's count (should not update LRU)
            tracker.GetMissingTreeCount("commit1");
            
            // Adding a fourth commit should still evict commit1 (oldest)
            tracker.AddMissingTree("tree4", "commit4");
            
            tracker.GetMissingTreeCount("commit1").ShouldEqual(0);
            tracker.GetMissingTreeCount("commit2").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit3").ShouldEqual(1);
            tracker.GetMissingTreeCount("commit4").ShouldEqual(1);
        }
    }
}
