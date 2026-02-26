using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Tracks missing trees per commit to support batching tree downloads.
    /// Maintains LRU eviction based on commits (not individual trees).
    /// </summary>
    public class MissingTreeTracker
    {
        private readonly int capacity;
        private readonly object syncLock = new object();

        // Primary storage: commit -> set of missing trees
        private readonly Dictionary<string, HashSet<string>> missingTreesByCommit;

        // Reverse lookup: tree -> commit (for fast lookups)
        private readonly Dictionary<string, string> commitByTree;

        // LRU ordering based on commits
        private readonly LinkedList<string> commitOrder;
        private readonly Dictionary<string, LinkedListNode<string>> commitNodes;

        public MissingTreeTracker(int capacity)
        {
            this.capacity = capacity;
            this.missingTreesByCommit = new Dictionary<string, HashSet<string>>();
            this.commitByTree = new Dictionary<string, string>();
            this.commitOrder = new LinkedList<string>();
            this.commitNodes = new Dictionary<string, LinkedListNode<string>>();
        }

        /// <summary>
        /// Records a missing tree for a commit. Marks the commit as recently used.
        /// </summary>
        public void AddMissingTree(string treeSha, string commitSha)
        {
            lock (this.syncLock)
            {
                // If tree already tracked for a different commit, remove it first
                if (this.commitByTree.TryGetValue(treeSha, out string existingCommit) && existingCommit != commitSha)
                {
                    this.RemoveTreeFromCommit(treeSha, existingCommit);
                }

                // Add or update the commit's missing trees
                if (!this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
                {
                    trees = new HashSet<string>();
                    this.missingTreesByCommit[commitSha] = trees;

                    // Check capacity and evict LRU commit if needed
                    if (this.commitNodes.Count >= this.capacity)
                    {
                        this.EvictLruCommit();
                    }

                    // Add new commit node to the front (MRU)
                    var node = this.commitOrder.AddFirst(commitSha);
                    this.commitNodes[commitSha] = node;
                }
                else
                {
                    // Move existing commit to front (mark as recently used)
                    this.MarkCommitAsUsed(commitSha);
                }

                trees.Add(treeSha);
                this.commitByTree[treeSha] = commitSha;
            }
        }

        /// <summary>
        /// Tries to get the commit associated with a tree SHA.
        /// </summary>
        public bool TryGetCommit(string treeSha, out string commitSha)
        {
            lock (this.syncLock)
            {
                if (this.commitByTree.TryGetValue(treeSha, out commitSha))
                {
                    // Mark the commit as recently used
                    this.MarkCommitAsUsed(commitSha);
                    return true;
                }

                commitSha = null;
                return false;
            }
        }

        /// <summary>
        /// Gets the count of missing trees for a specific commit.
        /// </summary>
        public int GetMissingTreeCount(string commitSha)
        {
            lock (this.syncLock)
            {
                if (this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
                {
                    return trees.Count;
                }

                return 0;
            }
        }

        /// <summary>
        /// Removes all missing trees associated with a commit (e.g., after downloading the commit pack).
        /// </summary>
        public void RemoveCommit(string commitSha)
        {
            lock (this.syncLock)
            {
                if (!this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
                {
                    return;
                }

                // Remove all tree -> commit reverse lookups
                foreach (var tree in trees)
                {
                    this.commitByTree.Remove(tree);
                }

                // Remove commit from primary storage
                this.missingTreesByCommit.Remove(commitSha);

                // Remove from LRU order
                if (this.commitNodes.TryGetValue(commitSha, out var node))
                {
                    this.commitOrder.Remove(node);
                    this.commitNodes.Remove(commitSha);
                }
            }
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
                this.RemoveCommit(lruCommit);
            }
        }

        private void RemoveTreeFromCommit(string treeSha, string commitSha)
        {
            if (this.missingTreesByCommit.TryGetValue(commitSha, out var trees))
            {
                trees.Remove(treeSha);

                // If no more trees for this commit, remove the commit entirely
                if (trees.Count == 0)
                {
                    this.RemoveCommit(commitSha);
                }
            }

            this.commitByTree.Remove(treeSha);
        }
    }
}
