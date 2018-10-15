using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [NonParallelizable]
    [Category(Categories.FullSuiteOnly)]
    [Category(Categories.WindowsOnly)]
    public class ConfigVerbTests : TestsWithMultiEnlistment
    {
        private const string ConfigFilePath = @"C:\ProgramData\GVFS\gvfs.config";

        [OneTimeSetUp]
        public void DeleteAllSettings()
        {
            if (File.Exists(ConfigFilePath))
            {
                File.Delete(ConfigFilePath);
            }
        }

        [TestCase, Order(1)]
        public void CreateValues()
        {
            this.RunConfigCommandAndCheckOutput("integerString 213", null);
            this.RunConfigCommandAndCheckOutput("integerString", new string[] { "213" });

            this.RunConfigCommandAndCheckOutput("floatString 213.15", null);
            this.RunConfigCommandAndCheckOutput("floatString", new string[] { "213.15" });

            this.RunConfigCommandAndCheckOutput("regularString foobar", null);
            this.RunConfigCommandAndCheckOutput("regularString", new string[] { "foobar" });

            this.RunConfigCommandAndCheckOutput("spacesString \"quick brown fox\"", null);
            this.RunConfigCommandAndCheckOutput("spacesString", new string[] { "quick brown fox" });
        }

        [TestCase, Order(2)]
        public void UpdateValues()
        {
            this.RunConfigCommandAndCheckOutput("integerString 314", null);
            this.RunConfigCommandAndCheckOutput("integerString", new string[] { "314" });

            this.RunConfigCommandAndCheckOutput("floatString 3.14159", null);
            this.RunConfigCommandAndCheckOutput("floatString", new string[] { "3.14159" });

            this.RunConfigCommandAndCheckOutput("regularString helloWorld!", null);
            this.RunConfigCommandAndCheckOutput("regularString", new string[] { "helloWorld!" });

            this.RunConfigCommandAndCheckOutput("spacesString \"jumped over lazy dog\"", null);
            this.RunConfigCommandAndCheckOutput("spacesString", new string[] { "jumped over lazy dog" });
        }

        [TestCase, Order(3)]
        public void ListValues()
        {
            string[] expectedSettings = new string[]
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
            string[] expectedSettings = new string[]
            {
                "integerString",
                "floatString",
                "regularString",
                "spacesString"
            };
            foreach (string keyValue in expectedSettings)
            {
                this.RunConfigCommandAndCheckOutput($"--delete {keyValue}", null);
            }

            this.RunConfigCommandAndCheckOutput($"--list", new string[] { string.Empty });
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
