using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// A fixed-capacity dictionary that evicts the least-recently-used entry when full.
    /// All operations are O(1) and thread-safe.
    /// </summary>
    /// <remarks>
    /// In future if we upgrade to .NET 10, we can replace this implementation with
    /// one using OrderedDictionary which was added in .NET 9.</remarks>
    public class LruCache<TKey, TValue>
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> order;
        private readonly object syncLock = new object();

        public LruCache(int capacity)
        {
            this.capacity = capacity;
            this.map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
            this.order = new LinkedList<KeyValuePair<TKey, TValue>>();
        }

        public int Count
        {
            get { lock (this.syncLock) { return this.map.Count; } }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (this.syncLock)
            {
                if (this.map.TryGetValue(key, out var node))
                {
                    this.order.Remove(node);
                    this.order.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }

                value = default(TValue);
                return false;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (this.syncLock)
            {
                if (this.map.TryGetValue(key, out var existing))
                {
                    this.order.Remove(existing);
                    this.map.Remove(key);
                }
                else if (this.map.Count >= this.capacity)
                {
                    var lru = this.order.Last;
                    this.order.RemoveLast();
                    this.map.Remove(lru.Value.Key);
                }

                var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
                this.order.AddFirst(node);
                this.map[key] = node;
            }
        }

        public bool Remove(TKey key)
        {
            lock (this.syncLock)
            {
                if (this.map.TryGetValue(key, out var node))
                {
                    this.order.Remove(node);
                    this.map.Remove(key);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns a snapshot of all entries in MRU to LRU order.
        /// </summary>
        public IList<KeyValuePair<TKey, TValue>> GetEntries()
        {
            lock (this.syncLock)
            {
                return new List<KeyValuePair<TKey, TValue>>(this.order);
            }
        }

        /// <summary>
        /// Removes all entries whose value equals <paramref name="value"/>.
        /// Returns the number of entries removed.
        /// </summary>
        public int RemoveAllWithValue(TValue value)
        {
            lock (this.syncLock)
            {
                int removed = 0;
                LinkedListNode<KeyValuePair<TKey, TValue>> node = this.order.First;
                while (node != null)
                {
                    LinkedListNode<KeyValuePair<TKey, TValue>> next = node.Next;
                    if (EqualityComparer<TValue>.Default.Equals(node.Value.Value, value))
                    {
                        this.map.Remove(node.Value.Key);
                        this.order.Remove(node);
                        removed++;
                    }

                    node = next;
                }

                return removed;
            }
        }
    }
}
