using NUnit.Framework;

namespace GVFS.Tests.Should
{
    public static class StringShouldExtensions
    {
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

        public static string ShouldNotContain(this string actualValue, params string[] unexpectedSubstrings)
        {
            foreach (string expectedSubstring in unexpectedSubstrings)
            {
                Assert.IsFalse(
                     actualValue.Contains(expectedSubstring),
                     "Unexpected substring '{0}' found in '{1}'",
                     expectedSubstring,
                     actualValue);
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
