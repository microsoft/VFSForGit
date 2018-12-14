using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitConfigHelperTests
    {
        [TestCase]
        public void SanitizeEmptyString()
        {
            string outputString;
            GitConfigHelper.TrySanitizeConfigFileLine(string.Empty, out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void SanitizePureWhiteSpace()
        {
            string outputString;
            GitConfigHelper.TrySanitizeConfigFileLine("   ", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine(" \t\t  ", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine(" \t\t\n\n  ", out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void SanitizeComment()
        {
            string outputString;
            GitConfigHelper.TrySanitizeConfigFileLine("# This is a comment ", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine("# This is a comment #", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine("## This is a comment ##", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine(" ## This is a comment ## ", out outputString).ShouldEqual(false);
            GitConfigHelper.TrySanitizeConfigFileLine("\t ## This is a comment ## \t ", out outputString).ShouldEqual(false);
        }

        [TestCase]
        public void TrimWhitspace()
        {
            string outputString;
            GitConfigHelper.TrySanitizeConfigFileLine(" // ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("//");

            GitConfigHelper.TrySanitizeConfigFileLine(" /* ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/*");

            GitConfigHelper.TrySanitizeConfigFileLine(" /A ", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigHelper.TrySanitizeConfigFileLine("\t /A \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigHelper.TrySanitizeConfigFileLine("  \t /A   \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");
        }

        [TestCase]
        public void TrimTrailingComment()
        {
            string outputString;
            GitConfigHelper.TrySanitizeConfigFileLine(" // # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("//");

            GitConfigHelper.TrySanitizeConfigFileLine(" /* # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/*");

            GitConfigHelper.TrySanitizeConfigFileLine(" /A # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigHelper.TrySanitizeConfigFileLine("\t /A \t # Trailing comment! \t", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");

            GitConfigHelper.TrySanitizeConfigFileLine("  \t /A   \t # Trailing comment!", out outputString).ShouldEqual(true);
            outputString.ShouldEqual("/A");
        }

        [TestCase]
        public void ParseKeyValuesTest()
        {
            string input = @"
core.gvfs=true
gc.auto=0
section.key=value1
section.key= value2
section.key =value3
section.key = value4
section.KEY=value5
section.empty=
";
            Dictionary<string, GitConfigSetting> result = GitConfigHelper.ParseKeyValues(input);

            result.Count.ShouldEqual(4);
            result["core.gvfs"].Values.Single().ShouldEqual("true");
            result["gc.auto"].Values.Single().ShouldEqual("0");
            result["section.key"].Values.Count.ShouldEqual(5);
            result["section.key"].Values.ShouldContain(v => v == "value1");
            result["section.key"].Values.ShouldContain(v => v == "value2");
            result["section.key"].Values.ShouldContain(v => v == "value3");
            result["section.key"].Values.ShouldContain(v => v == "value4");
            result["section.key"].Values.ShouldContain(v => v == "value5");
            result["section.empty"].Values.Single().ShouldEqual(string.Empty);
        }

        [TestCase]
        public void ParseSpaceSeparatedKeyValuesTest()
        {
            string input = @"
core.gvfs true
gc.auto 0
section.key value1
section.key  value2
section.key  value3
section.key   value4
section.KEY value5" +
"\nsection.empty ";

            Dictionary<string, GitConfigSetting> result = GitConfigHelper.ParseKeyValues(input, ' ');

            result.Count.ShouldEqual(4);
            result["core.gvfs"].Values.Single().ShouldEqual("true");
            result["gc.auto"].Values.Single().ShouldEqual("0");
            result["section.key"].Values.Count.ShouldEqual(5);
            result["section.key"].Values.ShouldContain(v => v == "value1");
            result["section.key"].Values.ShouldContain(v => v == "value2");
            result["section.key"].Values.ShouldContain(v => v == "value3");
            result["section.key"].Values.ShouldContain(v => v == "value4");
            result["section.key"].Values.ShouldContain(v => v == "value5");
            result["section.empty"].Values.Single().ShouldEqual(string.Empty);
        }

        [TestCase]
        public void GetSettingsTest()
        {
            string fileContents = @"
[core]
    gvfs = true
[gc]
    auto = 0
[section]
    key1 = 1
    key2 = 2
    key3 = 3
[notsection]
    keyN1 = N1
    keyN2 = N2
    keyN3 = N3
[section]
[section]
    key4 = 4
    key5 = 5
[section]
    key6 = 6
    key7 =
         = emptyKey";

            Dictionary<string, GitConfigSetting> result = GitConfigHelper.GetSettings(fileContents.Split('\r', '\n'), "Section");

            int expectedCount = 7; // empty keys will not be included.
            result.Count.ShouldEqual(expectedCount);

            // Verify keyN = N
            for (int i = 1; i <= expectedCount - 1; i++)
            {
                result["key" + i.ToString()].Values.ShouldContain(v => v == i.ToString());
            }

            // Verify empty value
            result["key7"].Values.Single().ShouldEqual(string.Empty);
        }
    }
}
