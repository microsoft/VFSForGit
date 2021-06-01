using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GVFS.Tests.Should
{
    public static class EnumerableShouldExtensions
    {
        public static IEnumerable<T> ShouldBeEmpty<T>(this IEnumerable<T> group, string message = null)
        {
            CollectionAssert.IsEmpty(group, message);
            return group;
        }

        public static IEnumerable<T> ShouldBeNonEmpty<T>(this IEnumerable<T> group)
        {
            CollectionAssert.IsNotEmpty(group);
            return group;
        }

        public static Dictionary<TKey, TValue> ShouldContain<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            TValue dictionaryValue;
            dictionary.TryGetValue(key, out dictionaryValue).ShouldBeTrue($"Dictionary {nameof(ShouldContain)} does not contain {key}");
            dictionaryValue.ShouldEqual(value, $"Dictionary {nameof(ShouldContain)} does not match on key {key} expected: {value} actual: {dictionaryValue}");

            return dictionary;
        }

        public static T ShouldContain<T>(this IEnumerable<T> group, Func<T, bool> predicate)
        {
            T item = group.FirstOrDefault(predicate);
            item.ShouldNotEqual(default(T), "No matching entries found in {" + string.Join(",", group.ToArray()) + "}");

            return item;
        }

        public static T ShouldContainSingle<T>(this IEnumerable<T> group, Func<T, bool> predicate)
        {
            T item = group.Single(predicate);
            item.ShouldNotEqual(default(T));

            return item;
        }

        public static void ShouldNotContain<T>(this IEnumerable<T> group, Func<T, bool> predicate)
        {
            T item = group.SingleOrDefault(predicate);
            item.ShouldEqual(default(T), "Unexpected matching entry found in {" + string.Join(",", group) + "}");
        }

        public static IEnumerable<T> ShouldNotContain<T>(this IEnumerable<T> group, IEnumerable<T> unexpectedValues, Func<T, T, bool> predicate)
        {
            List<T> groupList = new List<T>(group);

            foreach (T unexpectedValue in unexpectedValues)
            {
                Assert.IsFalse(groupList.Any(item => predicate(item, unexpectedValue)));
            }

            return group;
        }

        public static IEnumerable<T> ShouldContain<T>(this IEnumerable<T> group, IEnumerable<T> expectedValues, Func<T, T, bool> predicate)
        {
            List<T> groupList = new List<T>(group);

            foreach (T expectedValue in expectedValues)
            {
                Assert.IsTrue(groupList.Any(item => predicate(item, expectedValue)));
            }

            return group;
        }

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, params Action<T>[] itemCheckers)
        {
            List<T> groupList = new List<T>(group);
            List<Action<T>> itemCheckersList = new List<Action<T>>(itemCheckers);

            for (int i = 0; i < groupList.Count; i++)
            {
                itemCheckersList[i](groupList[i]);
            }

            return group;
        }

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, IEnumerable<T> expectedValues, Func<T, T, bool> equals, string message = "")
        {
            List<T> groupList = new List<T>(group);
            List<T> expectedValuesList = new List<T>(expectedValues);

            Comparer<T> comparer = new Comparer<T>(equals);
            List<T> groupExtraItems = groupList.Except(expectedValues, comparer).ToList();
            List<T> groupMissingItems = expectedValues.Except(groupList, comparer).ToList();

            StringBuilder errorMessage = new StringBuilder();

            if (groupList.Count != expectedValuesList.Count)
            {
                errorMessage.AppendLine(string.Format("{0} counts do not match. was: {1} expected: {2}", message, groupList.Count, expectedValuesList.Count));
            }

            foreach (T groupExtraItem in groupExtraItems)
            {
                errorMessage.AppendLine(string.Format("Extra: {0}", groupExtraItem));
            }

            foreach (T groupMissingItem in groupMissingItems)
            {
                errorMessage.AppendLine(string.Format("Missing: {0}", groupMissingItem));
            }

            if (!Enumerable.SequenceEqual(group, expectedValues, comparer))
            {
                errorMessage.AppendLine(string.Format("Items are not in the same order: '{0}' vs. '{1}'", string.Join(",", group), string.Join(",", expectedValues)));
            }

            if (errorMessage.Length > 0)
            {
                Assert.Fail("{0}\r\n{1}", message, errorMessage);
            }

            return group;
        }

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, params T[] expectedValues)
        {
            return group.ShouldMatchInOrder((IEnumerable<T>)expectedValues);
        }

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, IEnumerable<T> expectedValues)
        {
            return group.ShouldMatchInOrder(expectedValues, (t1, t2) => t1.Equals(t2));
        }

        private class Comparer<T> : IEqualityComparer<T>
        {
            private Func<T, T, bool> equals;

            public Comparer(Func<T, T, bool> equals)
            {
                this.equals = equals;
            }

            public bool Equals(T x, T y)
            {
                return this.equals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
