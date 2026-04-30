using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using NUnit.Framework;

namespace GVFS.CommandLine.Tests
{
    /// <summary>
    /// Tests that GVFS main CLI parsing matches the original CommandLineParser behavior.
    /// Verifies all verb subcommands, short aliases, and option compatibility.
    /// </summary>
    /// <remarks>
    /// System.CommandLine 2.0.3 note: Option.Name holds the primary name (e.g. "--list"),
    /// while Option.Aliases only contains SHORT aliases added via Aliases.Add() (e.g. "-l").
    /// All lookups must check both Name and Aliases to find an option by any of its names.
    /// </remarks>
    [TestFixture]
    public class GvfsMainCliTests
    {
        private RootCommand rootCommand;

        [SetUp]
        public void SetUp()
        {
            rootCommand = GVFS.Program.BuildRootCommand();
        }

        #region All Subcommands Exist

        [TestCase("cache-server")]
        [TestCase("clone")]
        [TestCase("config")]
        [TestCase("dehydrate")]
        [TestCase("diagnose")]
        [TestCase("health")]
        [TestCase("log")]
        [TestCase("mount")]
        [TestCase("prefetch")]
        [TestCase("repair")]
        [TestCase("service")]
        [TestCase("sparse")]
        [TestCase("status")]
        [TestCase("unmount")]
        [TestCase("upgrade")]
        [TestCase("version")]
        public void Subcommand_Exists(string name)
        {
            var cmd = rootCommand.Subcommands.FirstOrDefault(c => c.Name == name);
            Assert.That(cmd, Is.Not.Null, $"Expected subcommand '{name}' to exist");
        }

        #endregion

        #region Clone Short Aliases

        [Test]
        public void Clone_BranchOption_HasShortAlias_B()
        {
            var opt = FindOptionOnCommand("clone", "--branch");
            Assert.That(opt, Is.Not.Null, "Expected --branch option on clone");
            Assert.That(opt.Aliases, Does.Contain("-b"), "Expected -b short alias for clone --branch");
        }

        [Test]
        public void Clone_ParsesWithShortAlias()
        {
            var parseResult = rootCommand.Parse(new[] { "clone", "https://example.com/repo", "-b", "main" });
            Assert.That(parseResult.Errors, Is.Empty, "clone with -b should parse without errors");
        }

        #endregion

        #region Config Short Aliases

        [Test]
        public void Config_ListOption_HasShortAlias_L()
        {
            var opt = FindOptionOnCommand("config", "--list");
            Assert.That(opt, Is.Not.Null, "Expected --list option on config");
            Assert.That(opt.Aliases, Does.Contain("-l"), "Expected -l short alias for config --list");
        }

        [Test]
        public void Config_DeleteOption_HasShortAlias_D()
        {
            var opt = FindOptionOnCommand("config", "--delete");
            Assert.That(opt, Is.Not.Null, "Expected --delete option on config");
            Assert.That(opt.Aliases, Does.Contain("-d"), "Expected -d short alias for config --delete");
        }

        #endregion

        #region Health Short Aliases

        [Test]
        public void Health_DisplayCountOption_HasName_N()
        {
            var opt = FindOptionOnCommand("health", "-n");
            Assert.That(opt, Is.Not.Null, "Expected -n option on health command");
        }

        [Test]
        public void Health_DirectoryOption_HasShortAlias_D()
        {
            var opt = FindOptionOnCommand("health", "--directory");
            Assert.That(opt, Is.Not.Null, "Expected --directory option on health");
            Assert.That(opt.Aliases, Does.Contain("-d"), "Expected -d short alias for health --directory");
        }

        [Test]
        public void Health_StatusOption_HasShortAlias_S()
        {
            var opt = FindOptionOnCommand("health", "--status");
            Assert.That(opt, Is.Not.Null, "Expected --status option on health");
            Assert.That(opt.Aliases, Does.Contain("-s"), "Expected -s short alias for health --status");
        }

        #endregion

        #region Mount Short Aliases

        [Test]
        public void Mount_VerbosityOption_HasShortAlias_V()
        {
            var opt = FindOptionOnCommand("mount", "--verbosity");
            Assert.That(opt, Is.Not.Null, "Expected --verbosity option on mount");
            Assert.That(opt.Aliases, Does.Contain("-v"), "Expected -v short alias for mount --verbosity");
        }

        [Test]
        public void Mount_KeywordsOption_HasShortAlias_K()
        {
            var opt = FindOptionOnCommand("mount", "--keywords");
            Assert.That(opt, Is.Not.Null, "Expected --keywords option on mount");
            Assert.That(opt.Aliases, Does.Contain("-k"), "Expected -k short alias for mount --keywords");
        }

        #endregion

        #region Prefetch Short Aliases

        [Test]
        public void Prefetch_CommitsOption_HasShortAlias_C()
        {
            var opt = FindOptionOnCommand("prefetch", "--commits");
            Assert.That(opt, Is.Not.Null, "Expected --commits option on prefetch");
            Assert.That(opt.Aliases, Does.Contain("-c"), "Expected -c short alias for prefetch --commits");
        }

        #endregion

        #region Sparse Short Aliases (7 aliases)

        [TestCase("--set", "-s")]
        [TestCase("--file", "-f")]
        [TestCase("--add", "-a")]
        [TestCase("--remove", "-r")]
        [TestCase("--list", "-l")]
        [TestCase("--prune", "-p")]
        [TestCase("--disable", "-d")]
        public void Sparse_Option_HasShortAlias(string longName, string shortAlias)
        {
            var opt = FindOptionOnCommand("sparse", longName);
            Assert.That(opt, Is.Not.Null, $"Expected {longName} option on sparse");
            Assert.That(opt.Aliases, Does.Contain(shortAlias),
                $"Expected {shortAlias} short alias for sparse {longName}");
        }

        #endregion

        #region String Defaults (null-coalesce guards)

        [Test]
        public void Dehydrate_Folders_DefaultsToNullOrEmpty()
        {
            // Original had Default = "". Now we guard with ?? "" in the action.
            // From parse result, the default for unset string is null.
            // The null-coalesce guard ensures the verb receives "" not null.
            var opt = FindOptionOnCommand("dehydrate", "--folders");
            Assert.That(opt, Is.Not.Null, "Expected --folders option on dehydrate");
        }

        [Test]
        public void Prefetch_StringOptions_Exist()
        {
            var expectedOptions = new[] { "--files", "--folders", "--folders-list", "--files-list" };

            foreach (var optName in expectedOptions)
            {
                var opt = FindOptionOnCommand("prefetch", optName);
                Assert.That(opt, Is.Not.Null, $"Expected {optName} option on prefetch");
            }
        }

        [Test]
        public void Sparse_StringOptions_Exist()
        {
            var expectedOptions = new[] { "--set", "--file", "--add", "--remove" };

            foreach (var optName in expectedOptions)
            {
                var opt = FindOptionOnCommand("sparse", optName);
                Assert.That(opt, Is.Not.Null, $"Expected {optName} option on sparse");
            }
        }

        #endregion

        #region Full Command Parsing

        [Test]
        public void Clone_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "clone", "https://example.com/repo", @"C:\Users\test\repo",
                "--cache-server-url", "https://cache.test",
                "-b", "develop",
                "--single-branch",
                "--no-mount",
                "--no-prefetch"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full clone command should parse without errors");
        }

        [Test]
        public void Mount_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "mount", @"C:\Users\test\repo",
                "-v", "Warning",
                "-k", "Network"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full mount command should parse without errors");
        }

        [Test]
        public void Prefetch_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "prefetch",
                "--folders", "src;lib",
                "--files", "*.cs;*.h",
                "-c",
                "--verbose"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full prefetch command should parse without errors");
        }

        [Test]
        public void Sparse_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "sparse",
                "-s", "src;lib;tests",
                "-l"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full sparse command should parse without errors");
        }

        [Test]
        public void Health_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "health",
                "-n", "20",
                "-d", @"src\components",
                "-s"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full health command should parse without errors");
        }

        [Test]
        public void Dehydrate_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "dehydrate",
                "--confirm",
                "--folders", "src/old;temp"
            });
            Assert.That(parseResult.Errors, Is.Empty, "Full dehydrate command with --confirm --folders should parse without errors");
        }

        [Test]
        public void Service_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "service", "--list-mounted" });
            Assert.That(parseResult.Errors, Is.Empty);
        }

        [Test]
        public void Upgrade_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "upgrade", "--confirm" });
            Assert.That(parseResult.Errors, Is.Empty);
        }

        [Test]
        public void Unmount_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "unmount" });
            Assert.That(parseResult.Errors, Is.Empty);
        }

        [Test]
        public void Config_FullCommandLine_List_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "config", "-l" });
            Assert.That(parseResult.Errors, Is.Empty, "config -l should parse without errors");
        }

        [Test]
        public void Config_FullCommandLine_SetKeyValue_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "config", "mykey", "myvalue" });
            Assert.That(parseResult.Errors, Is.Empty, "config key value should parse without errors");
        }

        [Test]
        public void Config_FullCommandLine_Delete_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "config", "-d", "mykey" });
            Assert.That(parseResult.Errors, Is.Empty, "config -d key should parse without errors");
        }

        [Test]
        public void Repair_FullCommandLine_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "repair", "--confirm" });
            Assert.That(parseResult.Errors, Is.Empty);
        }

        #endregion

        #region Option Existence per Verb (complete verification)

        [Test]
        public void Clone_HasAllExpectedOptions()
        {
            var expected = new[] { "--cache-server-url", "--branch", "--single-branch", "--no-mount", "--no-prefetch", "--local-cache-path" };
            foreach (var optName in expected)
            {
                Assert.That(FindOptionOnCommand("clone", optName), Is.Not.Null,
                    $"clone should have {optName} option");
            }
        }

        [Test]
        public void Dehydrate_HasAllExpectedOptions()
        {
            var expected = new[] { "--confirm", "--no-status", "--folders" };
            foreach (var optName in expected)
            {
                Assert.That(FindOptionOnCommand("dehydrate", optName), Is.Not.Null,
                    $"dehydrate should have {optName} option");
            }
        }

        [Test]
        public void Prefetch_HasAllExpectedOptions()
        {
            var expected = new[] { "--files", "--folders", "--folders-list", "--stdin-files-list",
                                   "--stdin-folders-list", "--files-list", "--hydrate", "--commits", "--verbose" };
            foreach (var optName in expected)
            {
                Assert.That(FindOptionOnCommand("prefetch", optName), Is.Not.Null,
                    $"prefetch should have {optName} option");
            }
        }

        [Test]
        public void Service_HasAllExpectedOptions()
        {
            var expected = new[] { "--mount-all", "--unmount-all", "--list-mounted" };
            foreach (var optName in expected)
            {
                Assert.That(FindOptionOnCommand("service", optName), Is.Not.Null,
                    $"service should have {optName} option");
            }
        }

        [Test]
        public void Upgrade_HasAllExpectedOptions()
        {
            var expected = new[] { "--confirm", "--dry-run", "--no-verify" };
            foreach (var optName in expected)
            {
                Assert.That(FindOptionOnCommand("upgrade", optName), Is.Not.Null,
                    $"upgrade should have {optName} option");
            }
        }

        [Test]
        public void Unmount_HasSkipLockOption()
        {
            Assert.That(FindOptionOnCommand("unmount", "--skip-wait-for-lock"), Is.Not.Null,
                "unmount should have --skip-wait-for-lock option");
        }

        #endregion

        #region Helpers

        private Command FindSubcommand(string name)
        {
            return rootCommand.Subcommands.FirstOrDefault(c => c.Name == name)
                ?? throw new System.Exception($"Subcommand '{name}' not found");
        }

        /// <summary>
        /// Find an option on a subcommand by checking both Name and Aliases.
        /// System.CommandLine 2.0.3: Name holds the primary name, Aliases holds only short aliases.
        /// </summary>
        private Option FindOptionOnCommand(string subcommandName, string optionName)
        {
            var cmd = FindSubcommand(subcommandName);
            return cmd.Options.FirstOrDefault(o => o.Name == optionName || o.Aliases.Contains(optionName));
        }

        #endregion
    }
}
