using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Tests.Should
{
    public static class EnumerableShouldExtensions
    {
        public static IEnumerable<T> ShouldBeEmpty<T>(this IEnumerable<T> group)
        {
            CollectionAssert.IsEmpty(group);
            return group;
        }

        public static IEnumerable<T> ShouldBeNonEmpty<T>(this IEnumerable<T> group)
        {
            CollectionAssert.IsNotEmpty(group);
            return group;
        }

        public static T ShouldContain<T>(this IEnumerable<T> group, Func<T, bool> predicate)
        {
            T item = group.FirstOrDefault(predicate);
            item.ShouldNotEqual(default(T));

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
            item.ShouldEqual(default(T));
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

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, IEnumerable<T> expectedValues, Func<T, T, bool> equals)
        {
            List<T> groupList = new List<T>(group);
            List<T> expectedValuesList = new List<T>(expectedValues);

            groupList.Count.ShouldEqual(expectedValuesList.Count);

            for (int i = 0; i < groupList.Count; i++)
            {
                Assert.IsTrue(equals(groupList[i], expectedValuesList[i]), "Items at index {0} are not the same", i);
            }

            return group;
        }

        public static IEnumerable<T> ShouldMatchInOrder<T>(this IEnumerable<T> group, IEnumerable<T> expectedValues)
        {
            return group.ShouldMatchInOrder(expectedValues, (t1, t2) => t1.Equals(t2));
        }
    }
}
