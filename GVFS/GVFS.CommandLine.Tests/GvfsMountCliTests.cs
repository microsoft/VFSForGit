using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using NUnit.Framework;

namespace GVFS.CommandLine.Tests
{
    /// <summary>
    /// Tests that GVFS.Mount CLI parsing matches the original CommandLineParser behavior.
    /// Verifies defaults (not aliases — this is an internal tool called with long names).
    /// </summary>
    [TestFixture]
    public class GvfsMountCliTests
    {
        private RootCommand rootCommand;

        [SetUp]
        public void SetUp()
        {
            rootCommand = GVFS.Mount.Program.BuildRootCommand();
        }

        #region Default Values — Critical (these were previously broken)

        [Test]
        public void Verbosity_DefaultsToInformational()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo" });
            var opt = FindOption<string>("--verbosity");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("Informational"),
                "Verbosity should default to 'Informational' when not specified");
        }

        [Test]
        public void Keywords_DefaultsToAny()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo" });
            var opt = FindOption<string>("--keywords");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("Any"),
                "Keywords should default to 'Any' when not specified");
        }

        [Test]
        public void StartedByService_DefaultsToFalse()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo" });
            var opt = FindOption<string>("--StartedByService");
            Assert.That(opt, Is.Not.Null);
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("false"),
                "StartedByService should default to 'false' when not specified");
        }

        #endregion

        #region Defaults Are Not Aliases

        [Test]
        public void Informational_IsNotAnAlias()
        {
            var opt = FindOption("--verbosity");
            Assert.That(opt, Is.Not.Null);
            Assert.That(opt.Aliases, Does.Not.Contain("Informational"),
                "'Informational' should NOT be an alias for --verbosity");
        }

        [Test]
        public void Any_IsNotAnAlias()
        {
            var opt = FindOption("--keywords");
            Assert.That(opt, Is.Not.Null);
            Assert.That(opt.Aliases, Does.Not.Contain("Any"),
                "'Any' should NOT be an alias for --keywords");
        }

        [Test]
        public void False_IsNotAnAlias()
        {
            var opt = FindOption("--StartedByService");
            Assert.That(opt, Is.Not.Null);
            Assert.That(opt.Aliases, Does.Not.Contain("false"),
                "'false' should NOT be an alias for --StartedByService");
        }

        #endregion

        #region Explicit Value Parsing

        [Test]
        public void Verbosity_ExplicitValue_Overrides()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo", "--verbosity", "Verbose" });
            var opt = FindOption<string>("--verbosity");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("Verbose"));
        }

        [Test]
        public void Keywords_ExplicitValue_Overrides()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo", "--keywords", "Network" });
            var opt = FindOption<string>("--keywords");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("Network"));
        }

        [Test]
        public void StartedByService_ExplicitValue_Overrides()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo", "--StartedByService", "true" });
            var opt = FindOption<string>("--StartedByService");
            Assert.That(parseResult.GetValue(opt), Is.EqualTo("true"));
        }

        [Test]
        public void DebugWindow_DefaultsFalse()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo" });
            var opt = FindOption<bool>("--debug-window");
            Assert.That(parseResult.GetValue(opt), Is.False);
        }

        [Test]
        public void StartedByVerb_DefaultsFalse()
        {
            var parseResult = rootCommand.Parse(new[] { "C:\\repo" });
            var opt = FindOption<bool>("--StartedByVerb");
            Assert.That(parseResult.GetValue(opt), Is.False);
        }

        #endregion

        #region Argument Parsing

        [Test]
        public void EnlistmentRootPath_IsParsed()
        {
            var parseResult = rootCommand.Parse(new[] { @"C:\Users\test\repo" });
            var arg = rootCommand.Arguments.FirstOrDefault(a => a.Name == "enlistment-root-path");
            Assert.That(arg, Is.Not.Null);
            Assert.That(parseResult.GetValue((Argument<string>)arg), Is.EqualTo(@"C:\Users\test\repo"));
        }

        #endregion

        #region Full Command Line (matches how MountVerb launches GVFS.Mount.exe)

        [Test]
        public void MountVerbCommandLine_ParsesCorrectly()
        {
            // MountVerb constructs: GVFS.Mount <path> --verbosity Informational --keywords Any --StartedByVerb
            var parseResult = rootCommand.Parse(new[]
            {
                @"C:\Users\test\repo",
                "--verbosity", "Informational",
                "--keywords", "Any",
                "--StartedByVerb"
            });

            Assert.That(parseResult.Errors, Is.Empty, "MountVerb-style command line should parse without errors");

            var verbOpt = FindOption<string>("--verbosity");
            var kwOpt = FindOption<string>("--keywords");
            var verbStartedOpt = FindOption<bool>("--StartedByVerb");

            Assert.Multiple(() =>
            {
                Assert.That(parseResult.GetValue(verbOpt), Is.EqualTo("Informational"));
                Assert.That(parseResult.GetValue(kwOpt), Is.EqualTo("Any"));
                Assert.That(parseResult.GetValue(verbStartedOpt), Is.True);
            });
        }

        [Test]
        public void ServiceStartedCommandLine_ParsesCorrectly()
        {
            // MountVerb constructs when started by service:
            // GVFS.Mount <path> --verbosity Warning --keywords Network --StartedByService true
            var parseResult = rootCommand.Parse(new[]
            {
                @"C:\Users\test\repo",
                "--verbosity", "Warning",
                "--keywords", "Network",
                "--StartedByService", "true"
            });

            Assert.That(parseResult.Errors, Is.Empty);

            var verbOpt = FindOption<string>("--verbosity");
            var kwOpt = FindOption<string>("--keywords");
            var svcOpt = FindOption<string>("--StartedByService");

            Assert.Multiple(() =>
            {
                Assert.That(parseResult.GetValue(verbOpt), Is.EqualTo("Warning"));
                Assert.That(parseResult.GetValue(kwOpt), Is.EqualTo("Network"));
                Assert.That(parseResult.GetValue(svcOpt), Is.EqualTo("true"));
            });
        }

        #endregion

        #region All Expected Options Exist

        [Test]
        public void AllExpectedOptions_Exist()
        {
            var expectedOptions = new[]
            {
                "--verbosity", "--keywords", "--debug-window",
                "--StartedByService", "--StartedByVerb"
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
