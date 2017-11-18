using RGFS.GVFlt;
using RGFS.Tests.Should;
using NUnit.Framework;

namespace RGFS.UnitTests.GVFlt
{
    [TestFixture]
    public class PatternMatcherTests
    {
        private const char DOSStar = '<';
        private const char DOSQm = '>';
        private const char DOSDot = '"';

        [TestCase]
        public void EmptyStringsDoNotMatch()
        {
            PatternMatcher.StrictMatchPattern(null, "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern(string.Empty, "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern("Test", null).ShouldEqual(false);
            PatternMatcher.StrictMatchPattern("Test", string.Empty).ShouldEqual(false);
            PatternMatcher.StrictMatchPattern(null, null).ShouldEqual(false);
            PatternMatcher.StrictMatchPattern(string.Empty, string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IdenticalStringsMatch()
        {
            PatternMatcher.StrictMatchPattern("Test", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void MatchingIsCaseInsensitive()
        {
            PatternMatcher.StrictMatchPattern("Test", "TEST").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("TEST", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.TXT").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void WildCardSearchMatchesEverything()
        {
            PatternMatcher.StrictMatchPattern("*", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*.*", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*.*", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestLeadingStarPattern()
        {
            PatternMatcher.StrictMatchPattern("*est", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*EST", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("*.TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestLeadingDosStarPattern()
        {
            PatternMatcher.StrictMatchPattern(DOSStar + "est", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern(DOSStar + "EST", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern(DOSStar + "txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern(DOSStar + "TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestTrailingDosQmPattern()
        {
            PatternMatcher.StrictMatchPattern("Test" + DOSQm, "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("TEST" + DOSQm, "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSQm, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestQuestionMarkPattern()
        {
            PatternMatcher.StrictMatchPattern("???", "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern("????", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("?????", "Test").ShouldEqual(false);
        }

        [TestCase]
        public void TestMixedQuestionMarkPattern()
        {
            PatternMatcher.StrictMatchPattern("T?st", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("T?ST", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("T??ST", "Test").ShouldEqual(false);
        }

        [TestCase]
        public void TestMixedStarPattern()
        {
            PatternMatcher.StrictMatchPattern("T*est", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("T*t", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("T*T", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ر*يلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestMixedStarAndQuestionMarkPattern()
        {
            PatternMatcher.StrictMatchPattern("T*?est", "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern("T*?t", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("T*?", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("t*?", "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ر*يلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.?xt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestDosStarPattern()
        {
            PatternMatcher.StrictMatchPattern("T" + DOSStar, "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("t" + DOSStar + "txt", "Test.txt").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ر*يلٌأكتوبرû" + DOSStar + "TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
        }

        [TestCase]
        public void TestDosDotPattern()
        {
            PatternMatcher.StrictMatchPattern("Test" + DOSDot, "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("Test" + DOSDot, "Test.").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("Test" + DOSDot, "Test.txt").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt.").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt.temp").ShouldEqual(false);
        }

        [TestCase]
        public void TestDosQmPattern()
        {
            PatternMatcher.StrictMatchPattern(string.Concat(DOSQm, DOSQm, DOSQm), "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern(string.Concat(DOSQm, DOSQm, DOSQm, DOSQm), "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern(string.Concat(DOSQm, DOSQm, DOSQm, DOSQm, DOSQm), "Test").ShouldEqual(true);

            PatternMatcher.StrictMatchPattern(string.Concat("Te", DOSQm), "Test").ShouldEqual(false);
            PatternMatcher.StrictMatchPattern(string.Concat("TE", DOSQm, DOSQm), "Test").ShouldEqual(true);
            PatternMatcher.StrictMatchPattern(string.Concat("te", DOSQm, DOSQm, DOSQm), "Test").ShouldEqual(true);
        }
    }
}