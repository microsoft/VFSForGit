using RGFS.CommandLine;
using RGFS.Common;
using RGFS.Tests.Should;
using RGFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RGFS.UnitTests.CommandLine
{
    [TestFixture]
    public class HooksInstallerTests
    {
        private const string Filename = "hooksfile";

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void MergeHooksDataThrowsOnFoundRGFSHooks()
        {
            Assert.Throws<HooksInstaller.HooksConfigurationException>(
                () => HooksInstaller.MergeHooksData(new string[] { "first", "rgfs.hooks.exe" }, Filename, RGFSConstants.DotGit.Hooks.PreCommandHookName));
        }

        [TestCase]
        public void MergeHooksDataEmptyConfig()
        {
            string result = HooksInstaller.MergeHooksData(new string[] { }, Filename, RGFSConstants.DotGit.Hooks.PreCommandHookName);
            IEnumerable<string> resultLines = result
                .Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("#"));

            resultLines.Single().ShouldEqual(RGFSConstants.RGFSHooksExecutableName);
        }

        [TestCase]
        public void MergeHooksDataPreCommandLast()
        {
            string result = HooksInstaller.MergeHooksData(new string[] { "first", "second" }, Filename, RGFSConstants.DotGit.Hooks.PreCommandHookName);
            IEnumerable<string> resultLines = result
                .Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("#"));

            resultLines.Count().ShouldEqual(3);
            resultLines.ElementAt(0).ShouldEqual("first");
            resultLines.ElementAt(1).ShouldEqual("second");
            resultLines.ElementAt(2).ShouldEqual(RGFSConstants.RGFSHooksExecutableName);
        }

        [TestCase]
        public void MergeHooksDataPostCommandFirst()
        {
            string result = HooksInstaller.MergeHooksData(new string[] { "first", "second" }, Filename, RGFSConstants.DotGit.Hooks.PostCommandHookName);
            IEnumerable<string> resultLines = result
                .Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("#"));

            resultLines.Count().ShouldEqual(3);
            resultLines.ElementAt(0).ShouldEqual(RGFSConstants.RGFSHooksExecutableName);
            resultLines.ElementAt(1).ShouldEqual("first");
            resultLines.ElementAt(2).ShouldEqual("second");
        }

        [TestCase]
        public void MergeHooksDataDiscardBlankLines()
        {
            string result = HooksInstaller.MergeHooksData(new string[] { "first", "second", string.Empty, " " }, Filename, RGFSConstants.DotGit.Hooks.PreCommandHookName);
            IEnumerable<string> resultLines = result
                .Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("#"));

            resultLines.Count().ShouldEqual(3);
            resultLines.ElementAt(0).ShouldEqual("first");
            resultLines.ElementAt(1).ShouldEqual("second");
            resultLines.ElementAt(2).ShouldEqual(RGFSConstants.RGFSHooksExecutableName);
        }
    }
}
