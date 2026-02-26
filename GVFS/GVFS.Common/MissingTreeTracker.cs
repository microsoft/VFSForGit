using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Tracks missing trees per commit to support batching tree downloads.
    /// Maintains LRU eviction based on commits (not individual trees).
    /// A single tree SHA may be shared across multiple commits.
    /// </summary>
    public class MissingTreeTracker
    {
        private readonly int treeCapacity;
        private readonly object syncLock = new object();

        // Primary storage: commit -> set of missing trees
        private readonly Dictionary<string, HashSet<string>> missingTreesByCommit;

        // Reverse lookup: tree -> set of commits (for fast lookups)
        private readonly Dictionary<string, HashSet<string>> commitsByTree;

        // LRU ordering based on commits
        private readonly LinkedList<string> commitOrder;
        private readonly Dictionary<string, LinkedListNode<string>> commitNodes;

        public MissingTreeTracker(int treeCapacity)
        {
            this.treeCapacity = treeCapacity;
            this.missingTreesByCommit = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            this.commitsByTree = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            this.commitOrder = new LinkedList<string>();
            this.commitNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Records a missing root tree for a commit. Marks the commit as recently used.
        /// A tree may be associated with multiple commits.
        /// </summary>
        public void AddMissingRootTree(string treeSha, string commitSha)
        {
            lock (this.syncLock)
            {
                this.EnsureCommitTracked(commitSha);
                this.AddTreeToCommit(treeSha, commitSha);
            }
        }

        /// <summary>
        /// Records missing sub-trees discovered while processing a parent tree.
        /// Each sub-tree is associated with all commits currently tracking the parent tree.
        /// </summary>
        public void AddMissingSubTrees(string parentTreeSha, string[] subTreeShas)
        {
            lock (this.syncLock)
            {
                if (!this.commitsByTree.TryGetValue(parentTreeSha, out var commits))
                {
                    return;
                }

                // Snapshot the set because AddTreeToCommit may modify commitsByTree indirectly
                string[] commitSnapshot = commits.ToArray();
                foreach (string subTreeSha in subTreeShas)
                {
                    foreach (string commitSha in commitSnapshot)
                    {
                        /* Ensure it wasn't evicted earlier in the loop. */
                        if (!this.missingTreesByCommit.ContainsKey(commitSha))
                        {
                            continue;
                        }

                        this.AddTreeToCommit(subTreeSha, commitSha);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to get all commits associated with a tree SHA.
        /// Marks all found commits as recently used.
        /// </summary>
        public bool TryGetCommits(string treeSha, out string[] commitShas)
        {
            lock (this.syncLock)
            {
                if (this.commitsByTree.TryGetValue(treeSha, out var commits))
                {
                    commitShas = commits.ToArray();
                    foreach (string commitSha in commitShas)
                    {
                        this.MarkCommitAsUsed(commitSha);
                    }

                    return true;
                }

                commitShas = null;
                return false;
            }
        }

        /// <summary>
        /// Given a set of commits, finds the one with the most missing trees.
        /// </summary>
        public int GetHighestMissingTreeCount(string[] commitShas, out string highestCountCommitSha)
        {
            lock (this.syncLock)
            {
                highestCountCommitSha = null;
                int highestCount = 0;

                foreach (string commitSha in commitShas)
                {
                    if (this.missingTreesByCommit.TryGetValue(commitSha, out var trees)
                        && trees.Count > highestCount)
                    {
                        highestCount = trees.Count;
                        highestCountCommitSha = commitSha;
                    }
                }

                return highestCount;
            }
        }

        /// <summary>
        /// Marks a commit as complete (e.g. its pack was downloaded successfully).
        /// Because the trees are now available, they are also removed from tracking
        /// for any other commits that shared them, and those commits are cleaned up
        /// if they become empty.
        /// </summary>
        public void MarkCommitComplete(string commitSha)
        {
            lock (this.syncLock)
            {
                this.RemoveCommitWithCascade(commitSha);
            }
        }

        private void EnsureCommitTracked(string commitSha)
        {
            if (!this.missingTreesByCommit.TryGetValue(commitSha, out _))
            {
                this.missingTreesByCommit[commitSha] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var node = this.commitOrder.AddFirst(commitSha);
                this.commitNodes[commitSha] = node;
            }
            else
            {
                this.MarkCommitAsUsed(commitSha);
            }
        }

        private void AddTreeToCommit(string treeSha, string commitSha)
        {
            if (!this.commitsByTree.ContainsKey(treeSha))
            {
                // Evict LRU commits until there is room for the new tree
                while (this.commitsByTree.Count >= this.treeCapacity)
                {
                    this.EvictLruCommit();
                }

                this.commitsByTree[treeSha] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            this.missingTreesByCommit[commitSha].Add(treeSha);
            this.commitsByTree[treeSha].Add(commitSha);
        }

        private void MarkCommitAsUsed(string commitSha)
        {
            if (this.commitNodes.TryGetValue(commitSha, out var node))
            {
                this.commitOrder.Remove(node);
                var newNode = this.commitOrder.AddFirst(commitSha);
                this.commitNodes[commitSha] = newNode;
            }
        }

        private void EvictLruCommit()
        {
            if (this.commitOrder.Last != null)
            {
                string lruCommit = this.commitOrder.Last.Value;
                this.RemoveCommitNoCache(lruCommit);
            }
        }

        /// <summary>
        /// Removes a commit without cascading tree removal to other commits.
        /// Used during LRU eviction: the trees are still missing, so other commits
        /// that share those trees should continue to track them.
        /// </summary>
        private void RemoveCommitNoCache(string commitSha)
        {
            if (!this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
            {
                return;
            }

            foreach (string treeSha in trees)
            {
                if (this.commitsByTree.TryGetValue(treeSha, out var commits))
                {
                    commits.Remove(commitSha);
                    if (commits.Count == 0)
                    {
                        this.commitsByTree.Remove(treeSha);
                    }
                }
            }

            this.missingTreesByCommit.Remove(commitSha);
            this.RemoveFromLruOrder(commitSha);
        }

        /// <summary>
        /// Removes a commit and cascades: trees that were in this commit's set are
        /// also removed from all other commits that shared them. Any commit that
        /// becomes empty as a result is also removed (without further cascade).
        /// </summary>
        private void RemoveCommitWithCascade(string commitSha)
        {
            if (!this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
            {
                return;
            }

            // Collect commits that may become empty after we remove the shared trees.
            // We don't cascade further than one level.
            var commitsToCheck = new HashSet<string>();

            foreach (string treeSha in trees)
            {
                if (this.commitsByTree.TryGetValue(treeSha, out var sharingCommits))
                {
                    sharingCommits.Remove(commitSha);

                    foreach (string otherCommit in sharingCommits)
                    {
                        if (this.missingTreesByCommit.TryGetValue(otherCommit, out var otherTrees))
                        {
                            otherTrees.Remove(treeSha);
                            if (otherTrees.Count == 0)
                            {
                                commitsToCheck.Add(otherCommit);
                            }
                        }
                    }

                    sharingCommits.Clear();
                    this.commitsByTree.Remove(treeSha);
                }
            }

            this.missingTreesByCommit.Remove(commitSha);
            this.RemoveFromLruOrder(commitSha);

            // Clean up any commits that became empty due to the cascade
            foreach (string emptyCommit in commitsToCheck)
            {
                if (this.missingTreesByCommit.TryGetValue(emptyCommit, out var remaining) && remaining.Count == 0)
                {
                    this.missingTreesByCommit.Remove(emptyCommit);
                    this.RemoveFromLruOrder(emptyCommit);
                }
            }
        }

        private void RemoveFromLruOrder(string commitSha)
        {
            if (this.commitNodes.TryGetValue(commitSha, out var node))
            {
                this.commitOrder.Remove(node);
                this.commitNodes.Remove(commitSha);
            }
        }
    }
}
