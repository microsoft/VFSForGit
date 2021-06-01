using NUnit.Framework;
using System;

namespace GVFS.Tests.Should
{
    public static class StringShouldExtensions
    {
        public static int ShouldBeAnInt(this string value, string message)
        {
            int output;
            Assert.IsTrue(int.TryParse(value, out output), message);
            return output;
        }

        public static string ShouldContain(this string actualValue, params string[] expectedSubstrings)
        {
            foreach (string expectedSubstring in expectedSubstrings)
            {
                Assert.IsTrue(
                     actualValue.Contains(expectedSubstring),
                     "Expected substring '{0}' not found in '{1}'",
                     expectedSubstring,
                     actualValue);
            }

            return actualValue;
        }

        public static string ShouldNotContain(this string actualValue, bool ignoreCase, params string[] unexpectedSubstrings)
        {
            foreach (string unexpectedSubstring in unexpectedSubstrings)
            {
                if (ignoreCase)
                {
                    Assert.IsFalse(
                         actualValue.IndexOf(unexpectedSubstring, 0, StringComparison.OrdinalIgnoreCase) >= 0,
                         "Unexpected substring '{0}' found in '{1}'",
                         unexpectedSubstring,
                         actualValue);
                }
                else
                {
                    Assert.IsFalse(
                         actualValue.Contains(unexpectedSubstring),
                         "Unexpected substring '{0}' found in '{1}'",
                         unexpectedSubstring,
                         actualValue);
                }
            }

            return actualValue;
        }

        public static string ShouldContainOneOf(this string actualValue, params string[] expectedSubstrings)
        {
            for (int i = 0; i < expectedSubstrings.Length; i++)
            {
                if (actualValue.Contains(expectedSubstrings[i]))
                {
                    return actualValue;
                }
            }

            Assert.Fail("No expected substrings found in '{0}'", actualValue);
            return actualValue;
        }
    }
}
