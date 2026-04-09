using NUnit.Framework;
using System;
using System.CommandLine;
using System.IO;
using System.Text.RegularExpressions;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class VersionOutputTests
    {
        // Matches "GVFS X.Y.Z.W" with optional "+commitid" suffix
        private static readonly Regex VersionPattern = new Regex(
            @"^GVFS \d+\.\d+\.\d+\.\d+(\+\S+)?$",
            RegexOptions.Compiled);

        [TestCase("version")]
        [TestCase("--version")]
        public void VersionOutputMatchesExpectedFormat(string arg)
        {
            RootCommand rootCommand = GVFS.Program.BuildRootCommand();

            string output;
            TextWriter originalOut = Console.Out;
            try
            {
                using (StringWriter sw = new StringWriter())
                {
                    Console.SetOut(sw);
                    rootCommand.Parse(new[] { arg }).Invoke();
                    output = sw.ToString().Trim();
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.That(
                VersionPattern.IsMatch(output),
                "Expected 'GVFS X.Y.Z.W' format but got: " + output);
        }

        [Test]
        public void VersionAndDashDashVersionProduceSameOutput()
        {
            RootCommand rootCommand = GVFS.Program.BuildRootCommand();

            string versionOutput = CaptureOutput(rootCommand, "version");
            string dashDashOutput = CaptureOutput(rootCommand, "--version");

            Assert.AreEqual(versionOutput, dashDashOutput);
        }

        private static string CaptureOutput(RootCommand rootCommand, string arg)
        {
            TextWriter originalOut = Console.Out;
            try
            {
                using (StringWriter sw = new StringWriter())
                {
                    Console.SetOut(sw);
                    rootCommand.Parse(new[] { arg }).Invoke();
                    return sw.ToString().Trim();
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
