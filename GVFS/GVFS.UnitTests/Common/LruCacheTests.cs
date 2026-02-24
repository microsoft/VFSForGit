using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LruCacheTests
    {
        [TestCase]
        public void TryGetValue_ReturnsFalse_WhenEmpty()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            string value;
            cache.TryGetValue("missing", out value).ShouldEqual(false);
            value.ShouldBeNull();
        }

        [TestCase]
        public void Set_And_TryGetValue_ReturnsValue()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            cache.Set("key", "value");

            string value;
            cache.TryGetValue("key", out value).ShouldEqual(true);
            value.ShouldEqual("value");
        }

        [TestCase]
        public void Count_ReflectsCurrentSize()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            cache.Count.ShouldEqual(0);
            cache.Set("a", "1");
            cache.Count.ShouldEqual(1);
            cache.Set("b", "2");
            cache.Count.ShouldEqual(2);
            cache.Remove("a");
            cache.Count.ShouldEqual(1);
        }

        [TestCase]
        public void Set_OverwritesExistingKey()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            cache.Set("key", "first");
            cache.Set("key", "second");

            cache.Count.ShouldEqual(1);
            string value;
            cache.TryGetValue("key", out value).ShouldEqual(true);
            value.ShouldEqual("second");
        }

        [TestCase]
        public void Set_EvictsLRU_WhenAtCapacity()
        {
            LruCache<string, string> cache = new LruCache<string, string>(3);

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");

            // Adding a fourth entry should evict "a" (the oldest/LRU)
            cache.Set("d", "4");

            cache.Count.ShouldEqual(3);
            string value;
            cache.TryGetValue("a", out value).ShouldEqual(false);
            cache.TryGetValue("b", out value).ShouldEqual(true);
            cache.TryGetValue("c", out value).ShouldEqual(true);
            cache.TryGetValue("d", out value).ShouldEqual(true);
        }

        [TestCase]
        public void TryGetValue_PromotesToMRU_SoItIsNotNextEvicted()
        {
            LruCache<string, string> cache = new LruCache<string, string>(3);

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");

            // Access "a" to promote it to MRU — "b" becomes the LRU
            string value;
            cache.TryGetValue("a", out value);

            // Adding a fourth entry should now evict "b", not "a"
            cache.Set("d", "4");

            cache.Count.ShouldEqual(3);
            cache.TryGetValue("a", out value).ShouldEqual(true);
            cache.TryGetValue("b", out value).ShouldEqual(false);
            cache.TryGetValue("c", out value).ShouldEqual(true);
            cache.TryGetValue("d", out value).ShouldEqual(true);
        }

        [TestCase]
        public void Set_OverwriteExistingKey_DoesNotEvictOtherEntries()
        {
            LruCache<string, string> cache = new LruCache<string, string>(3);

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");

            // Overwriting an existing key must not count as a new entry and must not trigger eviction
            cache.Set("a", "updated");

            cache.Count.ShouldEqual(3);
            string value;
            cache.TryGetValue("a", out value).ShouldEqual(true);
            value.ShouldEqual("updated");
            cache.TryGetValue("b", out value).ShouldEqual(true);
            cache.TryGetValue("c", out value).ShouldEqual(true);
        }

        [TestCase]
        public void Remove_ReturnsTrueAndRemovesEntry()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);
            cache.Set("key", "value");

            cache.Remove("key").ShouldEqual(true);

            cache.Count.ShouldEqual(0);
            string value;
            cache.TryGetValue("key", out value).ShouldEqual(false);
        }

        [TestCase]
        public void Remove_ReturnsFalse_WhenKeyNotPresent()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            cache.Remove("nonexistent").ShouldEqual(false);
        }

        [TestCase]
        public void GetEntries_ReturnsSnapshotInMRUOrder()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");

            // Inserted a, b, c ? MRU order: c, b, a
            // Access "a" to promote it ? MRU order: a, c, b
            string value;
            cache.TryGetValue("a", out value);

            IList<KeyValuePair<string, string>> entries = cache.GetEntries();

            entries.Count.ShouldEqual(3);
            entries[0].Key.ShouldEqual("a");
            entries[1].Key.ShouldEqual("c");
            entries[2].Key.ShouldEqual("b");
        }

        [TestCase]
        public void GetEntries_ReturnsSnapshot_IndependentOfSubsequentMutations()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);
            cache.Set("a", "1");
            cache.Set("b", "2");

            IList<KeyValuePair<string, string>> snapshot = cache.GetEntries();
            cache.Set("c", "3");
            cache.Remove("a");

            // The snapshot must not be affected by mutations after it was taken
            snapshot.Count.ShouldEqual(2);
        }

        [TestCase]
        public void RemoveAllWithValue_RemovesAllMatchingEntries()
        {
            LruCache<string, string> cache = new LruCache<string, string>(10);

            cache.Set("tree1", "commitA");
            cache.Set("tree2", "commitA");
            cache.Set("tree3", "commitB");
            cache.Set("tree4", "commitA");

            int removed = cache.RemoveAllWithValue("commitA");

            removed.ShouldEqual(3);
            cache.Count.ShouldEqual(1);
            string value;
            cache.TryGetValue("tree1", out value).ShouldEqual(false);
            cache.TryGetValue("tree2", out value).ShouldEqual(false);
            cache.TryGetValue("tree4", out value).ShouldEqual(false);
            cache.TryGetValue("tree3", out value).ShouldEqual(true);
            value.ShouldEqual("commitB");
        }

        [TestCase]
        public void RemoveAllWithValue_RetainsNonMatchingEntries()
        {
            LruCache<string, string> cache = new LruCache<string, string>(10);

            cache.Set("tree1", "commitA");
            cache.Set("tree2", "commitB");
            cache.Set("tree3", "commitC");

            int removed = cache.RemoveAllWithValue("commitB");

            removed.ShouldEqual(1);
            cache.Count.ShouldEqual(2);
            string value;
            cache.TryGetValue("tree1", out value).ShouldEqual(true);
            cache.TryGetValue("tree2", out value).ShouldEqual(false);
            cache.TryGetValue("tree3", out value).ShouldEqual(true);
        }

        [TestCase]
        public void RemoveAllWithValue_ReturnsZero_WhenNoMatch()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);
            cache.Set("tree1", "commitA");

            int removed = cache.RemoveAllWithValue("commitX");

            removed.ShouldEqual(0);
            cache.Count.ShouldEqual(1);
        }

        [TestCase]
        public void RemoveAllWithValue_ReturnsZero_WhenEmpty()
        {
            LruCache<string, string> cache = new LruCache<string, string>(5);

            int removed = cache.RemoveAllWithValue("commitA");

            removed.ShouldEqual(0);
        }

        [TestCase]
        public void ThreadSafety_ConcurrentSetAndGet_DoesNotThrow()
        {
            const int threadCount = 8;
            const int operationsPerThread = 500;
            const int capacity = 20;

            LruCache<string, string> cache = new LruCache<string, string>(capacity);
            ManualResetEventSlim ready = new ManualResetEventSlim(false);

            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                tasks[t] = Task.Run(() =>
                {
                    ready.Wait();
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        string key = "key" + ((threadIndex * operationsPerThread + i) % (capacity * 2));
                        string value = "val" + i;

                        cache.Set(key, value);

                        string retrieved;
                        cache.TryGetValue(key, out retrieved);

                        if (i % 10 == 0)
                        {
                            cache.Remove(key);
                        }

                        if (i % 20 == 0)
                        {
                            cache.RemoveAllWithValue(value);
                        }
                    }
                });
            }

            ready.Set();
            Task.WaitAll(tasks);

            // No exceptions thrown and count is within valid bounds
            cache.Count.ShouldBeAtMost(capacity);
        }
    }
}
