using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using NUnit.Framework;

namespace GVFS.CommandLine.Tests
{
    /// <summary>
    /// Tests that FastFetch CLI parsing matches the original CommandLineParser behavior.
    /// Verifies short aliases, defaults, and option names are backward-compatible.
    /// </summary>
    [TestFixture]
    public class FastFetchCliTests
    {
        private RootCommand rootCommand;

        [SetUp]
        public void SetUp()
        {
            rootCommand = FastFetch.Program.BuildRootCommand();
        }

        #region Short Aliases

        [Test]
        public void CommitOption_HasShortAlias_C()
        {
            var opt = FindOption("--commit");
            Assert.That(opt, Is.Not.Null, "Expected --commit option to exist");
            Assert.That(opt.Aliases, Does.Contain("-c"), "Expected -c short alias for --commit");
        }

        [Test]
        public void BranchOption_HasShortAlias_B()
        {
            var opt = FindOption("--branch");
            Assert.That(opt, Is.Not.Null, "Expected --branch option to exist");
            Assert.That(opt.Aliases, Does.Contain("-b"), "Expected -b short alias for --branch");
        }

        [Test]
        public void MaxRetriesOption_HasShortAlias_R()
        {
            var opt = FindOption("--max-retries");
            Assert.That(opt, Is.Not.Null, "Expected --max-retries option to exist");
            Assert.That(opt.Aliases, Does.Contain("-r"), "Expected -r short alias for --max-retries");
        }

        [TestCase("-c", "abc123")]
        [TestCase("-b", "main")]
        [TestCase("-r", "5")]
        public void ShortAliases_ParseCorrectly(string alias, string value)
        {
            var parseResult = rootCommand.Parse(new[] { alias, value });
            Assert.That(parseResult.Errors, Is.Empty, $"Parsing '{alias} {value}' should produce no errors");
        }

        #endregion

        #region Default Values

        [Test]
        public void ChunkSize_DefaultsTo4000()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());
            var opt = FindOption<int>("--chunk-size");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(4000),
                "ChunkSize should default to 4000 when not specified");
        }

        [Test]
        public void MaxRetries_DefaultsTo10()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());
            var opt = FindOption<int>("--max-retries");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(10),
                "MaxRetries should default to 10 when not specified");
        }

        [Test]
        public void Folders_DefaultsToEmptyString()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());
            var opt = FindOption<string>("--folders");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(""),
                "Folders should default to empty string when not specified");
        }

        [Test]
        public void FoldersList_DefaultsToEmptyString()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());
            var opt = FindOption<string>("--folders-list");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(""),
                "FoldersList should default to empty string when not specified");
        }

        [Test]
        public void BooleanOptions_DefaultToFalse()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());

            var checkout = FindOption<bool>("--checkout");
            var forceCheckout = FindOption<bool>("--force-checkout");
            var verbose = FindOption<bool>("--verbose");
            var allowIndexMetadata = FindOption<bool>("--allow-index-metadata-update-from-working-tree");

            Assert.Multiple(() =>
            {
                Assert.That(parseResult.GetValue(checkout), Is.False, "--checkout should default to false");
                Assert.That(parseResult.GetValue(forceCheckout), Is.False, "--force-checkout should default to false");
                Assert.That(parseResult.GetValue(verbose), Is.False, "--verbose should default to false");
                Assert.That(parseResult.GetValue(allowIndexMetadata), Is.False, "--allow-index-metadata-update-from-working-tree should default to false");
            });
        }

        [Test]
        public void IntThreadOptions_DefaultToZero()
        {
            var parseResult = rootCommand.Parse(System.Array.Empty<string>());

            var search = FindOption<int>("--search-thread-count");
            var download = FindOption<int>("--download-thread-count");
            var index = FindOption<int>("--index-thread-count");
            var checkoutThread = FindOption<int>("--checkout-thread-count");

            Assert.Multiple(() =>
            {
                Assert.That(parseResult.GetValue(search), Is.EqualTo(0));
                Assert.That(parseResult.GetValue(download), Is.EqualTo(0));
                Assert.That(parseResult.GetValue(index), Is.EqualTo(0));
                Assert.That(parseResult.GetValue(checkoutThread), Is.EqualTo(0));
            });
        }

        #endregion

        #region Explicit Value Parsing

        [Test]
        public void ChunkSize_ExplicitValue_Overrides()
        {
            var parseResult = rootCommand.Parse(new[] { "--chunk-size", "8000" });
            var opt = FindOption<int>("--chunk-size");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(8000));
        }

        [Test]
        public void MaxRetries_ExplicitValue_Overrides()
        {
            var parseResult = rootCommand.Parse(new[] { "--max-retries", "3" });
            var opt = FindOption<int>("--max-retries");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(3));
        }

        [Test]
        public void CommitAndBranch_ParseWithShortAliases()
        {
            var parseResult = rootCommand.Parse(new[] { "-c", "abc123", "-b", "feature/test" });
            var commitOpt = FindOption<string>("--commit");
            var branchOpt = FindOption<string>("--branch");
            Assert.Multiple(() =>
            {
                Assert.That(parseResult.GetValue(commitOpt), Is.EqualTo("abc123"));
                Assert.That(parseResult.GetValue(branchOpt), Is.EqualTo("feature/test"));
            });
        }

        [Test]
        public void AllStringOptions_ParseCorrectly()
        {
            var parseResult = rootCommand.Parse(new[]
            {
                "--commit", "abc123",
                "--branch", "main",
                "--cache-server-url", "https://cache.example.com",
                "--git-path", @"C:\Program Files\Git\bin\git.exe",
                "--folders", "src;lib",
                "--folders-list", @"C:\folders.txt",
                "--parent-activity-id", "12345678-1234-1234-1234-123456789012"
            });

            Assert.That(parseResult.Errors, Is.Empty, "All string options should parse without errors");
        }

        [Test]
        public void MaxRetries_ShortAlias_R_ParsesCorrectly()
        {
            var parseResult = rootCommand.Parse(new[] { "-r", "5" });
            var opt = FindOption<int>("--max-retries");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo(5));
        }

        #endregion

        #region All Expected Options Exist

        [Test]
        public void AllExpectedOptions_Exist()
        {
            var expectedOptions = new[]
            {
                "--commit", "--branch", "--cache-server-url", "--chunk-size",
                "--checkout", "--force-checkout", "--search-thread-count",
                "--download-thread-count", "--index-thread-count", "--checkout-thread-count",
                "--max-retries", "--git-path", "--folders", "--folders-list",
                "--allow-index-metadata-update-from-working-tree", "--verbose",
                "--parent-activity-id"
            };

            foreach (var optName in expectedOptions)
            {
                Assert.That(FindOption(optName), Is.Not.Null, $"Expected option {optName} to exist");
            }
        }

        #endregion

        #region Helpers

        private Option FindOption(string name)
        {
            return rootCommand.Options.FirstOrDefault(o => o.Name == name || o.Aliases.Contains(name));
        }

        private Option<T> FindOption<T>(string name)
        {
            return rootCommand.Options.FirstOrDefault(o => o.Name == name || o.Aliases.Contains(name)) as Option<T>;
        }

        #endregion
    }
}
