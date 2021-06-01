using NUnit.Framework;
using System;

namespace GVFS.Tests.Should
{
    public static class ValueShouldExtensions
    {
        public static bool ShouldBeTrue(this bool actualValue, string message = "")
        {
            actualValue.ShouldEqual(true, message);
            return actualValue;
        }

        public static bool ShouldBeFalse(this bool actualValue, string message = "")
        {
            actualValue.ShouldEqual(false, message);
            return actualValue;
        }

        public static T ShouldBeAtLeast<T>(this T actualValue, T expectedValue, string message = "") where T : IComparable
        {
            Assert.GreaterOrEqual(actualValue, expectedValue, message);
            return actualValue;
        }

        public static T ShouldBeAtMost<T>(this T actualValue, T expectedValue, string message = "") where T : IComparable
        {
            Assert.LessOrEqual(actualValue, expectedValue, message);
            return actualValue;
        }

        public static T ShouldEqual<T>(this T actualValue, T expectedValue, string message = "")
        {
            Assert.AreEqual(expectedValue, actualValue, message);
            return actualValue;
        }

        public static T[] ShouldEqual<T>(this T[] actualValue, T[] expectedValue, int start, int count)
        {
            expectedValue.Length.ShouldBeAtLeast(start + count);
            for (int i = 0; i < count; ++i)
            {
                actualValue[i].ShouldEqual(expectedValue[i + start]);
            }

            return actualValue;
        }

        public static T ShouldNotEqual<T>(this T actualValue, T unexpectedValue, string message = "")
        {
            Assert.AreNotEqual(unexpectedValue, actualValue, message);
            return actualValue;
        }

        public static T ShouldBeSameAs<T>(this T actualValue, T expectedValue, string message = "")
        {
            Assert.AreSame(expectedValue, actualValue, message);
            return actualValue;
        }

        public static T ShouldNotBeSameAs<T>(this T actualValue, T expectedValue, string message = "")
        {
            Assert.AreNotSame(expectedValue, actualValue, message);
            return actualValue;
        }

        public static T ShouldBeOfType<T>(this object obj)
        {
            Assert.IsTrue(obj is T, "Expected type {0}, but the object is actually of type {1}", typeof(T), obj.GetType());
            return (T)obj;
        }

        public static void ShouldBeNull<T>(this T obj, string message = "")
            where T : class
        {
            Assert.IsNull(obj, message);
        }

        public static T ShouldNotBeNull<T>(this T obj, string message = "")
            where T : class
        {
            Assert.IsNotNull(obj, message);
            return obj;
        }
    }
}
