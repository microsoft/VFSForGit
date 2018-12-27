using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GVFS.Common
{
    public class ConcurrentHashSet<T> : IEnumerable<T>
    {
        private ConcurrentDictionary<T, bool> dictionary;

        public ConcurrentHashSet()
        {
            this.dictionary = new ConcurrentDictionary<T, bool>();
        }

        public ConcurrentHashSet(IEqualityComparer<T> comparer)
        {
            this.dictionary = new ConcurrentDictionary<T, bool>(comparer);
        }

        public int Count
        {
            get { return this.dictionary.Count; }
        }

        public bool Add(T entry)
        {
            return this.dictionary.TryAdd(entry, true);
        }

        public bool Contains(T item)
        {
            return this.dictionary.ContainsKey(item);
        }

        public void Clear()
        {
            this.dictionary.Clear();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return this.dictionary.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public bool TryRemove(T key)
        {
            bool value;
            return this.dictionary.TryRemove(key, out value);
        }
    }
}
