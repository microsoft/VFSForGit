using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [NonParallelizable]
    [Category(Categories.MacTODO.M4)]
    public class ConfigVerbTests : TestsWithMultiEnlistment
    {
        [OneTimeSetUp]
        public void DeleteAllSettings()
        {
            string configFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GVFS",
                "gvfs.config");

            if (File.Exists(configFilePath))
            {
                File.Delete(configFilePath);
            }
        }

        [TestCase, Order(1)]
        public void CreateValues()
        {
            this.RunConfigCommandAndCheckOutput("integerString 213", null);
            this.RunConfigCommandAndCheckOutput("integerString", new[] { "213" });

            this.RunConfigCommandAndCheckOutput("floatString 213.15", null);
            this.RunConfigCommandAndCheckOutput("floatString", new[] { "213.15" });

            this.RunConfigCommandAndCheckOutput("regularString foobar", null);
            this.RunConfigCommandAndCheckOutput("regularString", new[] { "foobar" });

            this.RunConfigCommandAndCheckOutput("spacesString \"quick brown fox\"", null);
            this.RunConfigCommandAndCheckOutput("spacesString", new[] { "quick brown fox" });
        }

        [TestCase, Order(2)]
        public void UpdateValues()
        {
            this.RunConfigCommandAndCheckOutput("integerString 314", null);
            this.RunConfigCommandAndCheckOutput("integerString", new[] { "314" });

            this.RunConfigCommandAndCheckOutput("floatString 3.14159", null);
            this.RunConfigCommandAndCheckOutput("floatString", new[] { "3.14159" });

            this.RunConfigCommandAndCheckOutput("regularString helloWorld!", null);
            this.RunConfigCommandAndCheckOutput("regularString", new[] { "helloWorld!" });

            this.RunConfigCommandAndCheckOutput("spacesString \"jumped over lazy dog\"", null);
            this.RunConfigCommandAndCheckOutput("spacesString", new[] { "jumped over lazy dog" });
        }

        [TestCase, Order(3)]
        public void ListValues()
        {
            string[] expectedSettings = new[]
            {
                "integerString=314",
                "floatString=3.1415",
                "regularString=helloWorld!",
                "spacesString=jumped over lazy dog"
            };
            this.RunConfigCommandAndCheckOutput("--list", expectedSettings);
        }

        [TestCase, Order(4)]
        public void DeleteValues()
        {
            string[] settingsToDelete = new[]
            {
                "integerString",
                "floatString",
                "regularString",
                "spacesString"
            };
            foreach (string keyValue in settingsToDelete)
            {
                this.RunConfigCommandAndCheckOutput($"--delete {keyValue}", null);
            }

            this.RunConfigCommandAndCheckOutput($"--list", new[] { string.Empty });
        }

        [TestCase, Order(5)]
        public void ValidateMutuallyExclusiveOptions()
        {
            ProcessResult result = ProcessHelper.Run(GVFSTestConfig.PathToGVFS, "config --list --delete foo");
            result.Errors.ShouldContain(new[] { "ERROR", "list", "delete", "is not compatible with" });
        }

        private void RunConfigCommandAndCheckOutput(string argument, string[] expectedOutput)
        {
            GVFSProcess gvfsProcess = new GVFSProcess(
                GVFSTestConfig.PathToGVFS,
                enlistmentRoot: null,
                localCacheRoot: null);

            string result = gvfsProcess.RunConfigVerb(argument);
            if (expectedOutput != null)
            {
                foreach (string output in expectedOutput)
                {
                    result.ShouldContain(expectedOutput);
                }
            }
        }
    }
}
