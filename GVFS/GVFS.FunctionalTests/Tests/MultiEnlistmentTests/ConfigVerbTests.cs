using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [Category(Categories.FullSuiteOnly)]
    [Category(Categories.MacTODO.M4)]
    public class ConfigVerbTests : TestsWithMultiEnlistment
    {
        private const string IntegerSettingKey = "functionalTest_Integer";
        private const string FloatSettingKey = "functionalTest_Float";
        private const string RegularStringSettingKey = "functionalTest_RegularString";
        private const string SpacedStringSettingKey = "functionalTest_SpacedString";

        private readonly Dictionary<string, string> initialSettings = new Dictionary<string, string>()
        {
            { IntegerSettingKey, "213" },
            { FloatSettingKey, "213.15" },
            { RegularStringSettingKey, "foobar" },
            { SpacedStringSettingKey, "quick brown fox" }
        };

        private readonly Dictionary<string, string> updateSettings = new Dictionary<string, string>()
        {
            { IntegerSettingKey, "32123" },
            { FloatSettingKey, "3.14159" },
            { RegularStringSettingKey, "helloWorld!" },
            { SpacedStringSettingKey, "jumped over lazy dog" }
        };

        [OneTimeSetUp]
        public void ResetTestConfig()
        {
            this.DeleteSettings(this.initialSettings);
            this.DeleteSettings(this.updateSettings);
        }

        [TestCase, Order(1)]
        public void CreateSettings()
        {
            this.ApplySettings(this.initialSettings);
            this.ConfigShouldContainSettings(this.initialSettings);
        }

        [TestCase, Order(2)]
        public void UpdateSettings()
        {
            this.ApplySettings(this.updateSettings);
            this.ConfigShouldContainSettings(this.updateSettings);
        }

        [TestCase, Order(3)]
        public void ListSettings()
        {
            this.ConfigShouldContainSettings(this.updateSettings);
        }

        [TestCase, Order(4)]
        public void ReadSingleSetting()
        {
            foreach (KeyValuePair<string, string> setting in this.updateSettings)
            {
                string value = this.RunConfigCommand($"{setting.Key}");
                value.TrimEnd(Environment.NewLine.ToCharArray()).ShouldEqual($"{setting.Value}");
            }
        }

        [TestCase, Order(5)]
        public void DeleteSettings()
        {
            this.DeleteSettings(this.updateSettings);

            List<string> deletedLines = new List<string>();
            foreach (KeyValuePair<string, string> setting in this.updateSettings)
            {
                deletedLines.Add(this.GetSettingLineInConfigFileFormat(setting));
            }

            string allSettings = this.RunConfigCommand("--list");
            allSettings.ShouldNotContain(ignoreCase: true, unexpectedSubstrings: deletedLines.ToArray());
        }

        private void DeleteSettings(Dictionary<string, string> settings)
        {
            List<string> deletedLines = new List<string>();
            foreach (KeyValuePair<string, string> setting in settings)
            {
                this.RunConfigCommand($"--delete {setting.Key}");
            }
        }

        private void ConfigShouldContainSettings(Dictionary<string, string> expectedSettings)
        {
            List<string> expectedLines = new List<string>();
            foreach (KeyValuePair<string, string> setting in expectedSettings)
            {
                expectedLines.Add(this.GetSettingLineInConfigFileFormat(setting));
            }

            string allSettings = this.RunConfigCommand("--list");
            allSettings.ShouldContain(expectedLines.ToArray());
        }

        private string GetSettingLineInConfigFileFormat(KeyValuePair<string, string> setting)
        {
            return $"{setting.Key}={setting.Value}";
        }

        private void ApplySettings(Dictionary<string, string> settings)
        {
            foreach (KeyValuePair<string, string> setting in settings)
            {
                this.RunConfigCommand($"{setting.Key} \"{setting.Value}\"");
            }
        }

        private string RunConfigCommand(string argument)
        {
            ProcessResult result = ProcessHelper.Run(GVFSTestConfig.PathToGVFS, $"config {argument}");
            result.ExitCode.ShouldEqual(0, result.Errors);

            return result.Output;
        }
    }
}
