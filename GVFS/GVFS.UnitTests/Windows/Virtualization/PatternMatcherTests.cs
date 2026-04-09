using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using Microsoft.Windows.ProjFS;
using NUnit.Framework;

namespace GVFS.UnitTests.Windows.Virtualization
{
    [TestFixture]
    public class PatternMatcherTests
    {
        private const char DOSStar = '<';
        private const char DOSQm = '>';
        private const char DOSDot = '"';

        [TestCase]
        public void EmptyPatternShouldMatch()
        {
            PatternShouldMatch(null, "Test");
            PatternShouldMatch(string.Empty, "Test");
        }

        [TestCase]
        public void EmptyNameDoesNotMatch()
        {
            PatternShouldNotMatch("Test", null);
            PatternShouldNotMatch("Test", string.Empty);
            PatternShouldNotMatch(null, null);
            PatternShouldNotMatch(string.Empty, string.Empty);
        }

        [TestCase]
        public void IdenticalStringsMatch()
        {
            PatternShouldMatch("Test", "Test");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void MatchingIsCaseInsensitive()
        {
            PatternShouldMatch("Test", "TEST");
            PatternShouldMatch("TEST", "Test");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.TXT");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void WildCardSearchMatchesEverything()
        {
            PatternShouldMatch("*", "Test");
            PatternShouldNotMatch("*.*", "Test");
            PatternShouldMatch("*", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
            PatternShouldMatch("*.*", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestLeadingStarPattern()
        {
            PatternShouldMatch("*est", "Test");
            PatternShouldMatch("*EST", "Test");
            PatternShouldMatch("*txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
            PatternShouldMatch("*.TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestLeadingDosStarPattern()
        {
            PatternShouldMatch(DOSStar + "est", "Test");
            PatternShouldMatch(DOSStar + "EST", "Test");
            PatternShouldMatch(DOSStar + "txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
            PatternShouldMatch(DOSStar + "TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestTrailingDosQmPattern()
        {
            PatternShouldMatch("Test" + DOSQm, "Test");
            PatternShouldMatch("TEST" + DOSQm, "Test");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSQm, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestQuestionMarkPattern()
        {
            PatternShouldNotMatch("???", "Test");
            PatternShouldMatch("????", "Test");
            PatternShouldNotMatch("?????", "Test");
        }

        [TestCase]
        public void TestMixedQuestionMarkPattern()
        {
            PatternShouldMatch("T?st", "Test");
            PatternShouldMatch("T?ST", "Test");
            PatternShouldNotMatch("T??ST", "Test");
        }

        [TestCase]
        public void TestMixedStarPattern()
        {
            PatternShouldMatch("T*est", "Test");
            PatternShouldMatch("T*t", "Test");
            PatternShouldMatch("T*T", "Test");
            PatternShouldMatch("ر*يلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestMixedStarAndQuestionMarkPattern()
        {
            PatternShouldNotMatch("T*?est", "Test");
            PatternShouldMatch("T*?t", "Test");
            PatternShouldMatch("T*?", "Test");
            PatternShouldMatch("t*?", "Test");
            PatternShouldMatch("ر*يلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.?xt", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestDosStarPattern()
        {
            PatternShouldMatch("T" + DOSStar, "Test");
            PatternShouldMatch("t" + DOSStar + "txt", "Test.txt");
            PatternShouldMatch("ر*يلٌأكتوبرû" + DOSStar + "TXT", "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
        }

        [TestCase]
        public void TestDosDotPattern()
        {
            PatternShouldMatch("Test" + DOSDot, "Test");
            PatternShouldMatch("Test" + DOSDot, "Test.");
            PatternShouldNotMatch("Test" + DOSDot, "Test.txt");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt");
            PatternShouldMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt.");
            PatternShouldNotMatch("ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt" + DOSDot, "ريلٌأكتوبرûمارسأغسطسºٰٰۂْٗ۵ريلٌأك.txt.temp");
        }

        [TestCase]
        public void TestDosQmPattern()
        {
            PatternShouldNotMatch(string.Concat(DOSQm, DOSQm, DOSQm), "Test");
            PatternShouldMatch(string.Concat(DOSQm, DOSQm, DOSQm, DOSQm), "Test");
            PatternShouldMatch(string.Concat(DOSQm, DOSQm, DOSQm, DOSQm, DOSQm), "Test");

            PatternShouldNotMatch(string.Concat("Te", DOSQm), "Test");
            PatternShouldMatch(string.Concat("TE", DOSQm, DOSQm), "Test");
            PatternShouldMatch(string.Concat("te", DOSQm, DOSQm, DOSQm), "Test");
        }

        private static void PatternShouldMatch(string filter, string name)
        {
            PatternMatcher.StrictMatchPattern(filter, name).ShouldBeTrue();
            Utils.IsFileNameMatch(name, filter).ShouldBeTrue();
        }

        private static void PatternShouldNotMatch(string filter, string name)
        {
            PatternMatcher.StrictMatchPattern(filter, name).ShouldBeFalse();
            Utils.IsFileNameMatch(name, filter).ShouldBeFalse();
        }
    }
}