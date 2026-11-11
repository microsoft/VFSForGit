using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    public class AuthenticationConfigurationTests
    {
        private const string Secret = "SecretSentinelNotForTelemetry";
        private string root;
        private string gitPath;

        [SetUp]
        public void Setup()
        {
            this.gitPath = GitTestHelper.FindGitOnPath();
            this.root = Path.Combine(Path.GetTempPath(), nameof(AuthenticationConfigurationTests) + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(this.root, ".git", "objects"));
            Directory.CreateDirectory(Path.Combine(this.root, ".git", "refs", "heads"));
            File.WriteAllText(Path.Combine(this.root, ".git", "HEAD"), "ref: refs/heads/main\n");
            File.WriteAllText(Path.Combine(this.root, ".git", "config"), "[core]\nrepositoryformatversion = 0\nbare = false\n");
            File.WriteAllText(Path.Combine(this.root, "global"), string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(this.root, recursive: true);
        }

        [TestCase("manager", "manager")]
        [TestCase("", "reset")]
        [TestCase(null, "omitted:valueless")]
        [TestCase("!echo " + Secret, "omitted:shell")]
        [TestCase("manager --token=" + Secret, "manager:omitted-arguments")]
        [TestCase("unknown-" + Secret, "omitted:unknown-or-arguments")]
        [TestCase("\"C:/Users/" + Secret + "/Git/ucrt64/bin/git-credential-manager.exe\"", "<git-install>/ucrt64/bin/git-credential-manager.exe")]
        [TestCase("C:/Users/" + Secret + "/mingw64/bin/git-credential-manager.exe", "<git-install>/mingw64/bin/git-credential-manager.exe")]
        [TestCase("C:\\Users\\" + Secret + "\\Git\\clangarm64\\bin\\git-credential-manager.exe", "<git-install>/clangarm64/bin/git-credential-manager.exe")]
        [TestCase("\"C:\\Users\\" + Secret + "\\Git ARM64\\CLANGARM64\\bin\\git-credential-manager.exe\"", "<git-install>/clangarm64/bin/git-credential-manager.exe")]
        [TestCase("C:/Users/" + Secret + "/git-credential-manager-core.exe", "<redacted-path>/git-credential-manager-core.exe")]
        [TestCase("C:/tool --token=" + Secret + "/git-credential-manager.exe", "omitted:unknown-or-arguments")]
        [TestCase("C:/tools/git-credential-manager.exe;" + Secret, "omitted:unknown-or-arguments")]
        public void HelpersExposeOnlyClosedDescriptions(string value, string expected)
        {
            Assert.That(AuthenticationConfiguration.DescribeHelper(value), Is.EqualTo(expected));
        }

        [TestCase("https://example.com/org/repo", new[] { "manager", "cache", "wincred", "store", "oauth", "osxkeychain" })]
        [TestCase("https://example.com/org/other", new[] { "manager", "cache", "wincred", "store", "osxkeychain" })]
        [TestCase("https://example.com/organization/repo", new[] { "manager", "cache", "wincred", "osxkeychain" })]
        [TestCase("https://example.com:443/org/repo", new[] { "manager", "cache", "wincred", "store", "oauth", "osxkeychain" })]
        [TestCase("https://example.com/%6frg/repo%2F", new[] { "manager", "cache", "wincred", "store", "oauth", "osxkeychain" })]
        [TestCase("https://user@" + "example.com/org/repo", new[] { "manager", "cache", "wincred", "store", "oauth", "libsecret", "osxkeychain" })]
        [TestCase("https://other.example.com/org/repo", new[] { "manager", "cache", "osxkeychain" })]
        public void NativeGitMatchingPreservesAllApplicableHelpers(string url, string[] expected)
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nhelper = manager\nhelper = cache\n" +
                "[credential \"https://example.com\"]\nhelper = wincred\n" +
                "[credential \"https://example.com/org\"]\nhelper = store\n" +
                "[credential \"https://example.com/org/repo\"]\nhelper = oauth\n" +
                "[credential \"https://user@example.com/org\"]\nhelper = libsecret\n" +
                "[credential]\nhelper = osxkeychain\n");

            EventMetadata snapshot = this.Collect(url);
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo(string.Join(", ", expected)));
            Assert.That(snapshot["UseHttpPath"], Is.EqualTo("false"));
        }

        [Test]
        public void IncludesLocalWorktreeAndCommandResetsPreserveOrderWithoutSecrets()
        {
            File.WriteAllText(Path.Combine(this.root, "included"), "[credential]\nhelper = cache\n");
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential]\nhelper = manager\n[include]\npath = included\n");
            File.AppendAllText(Path.Combine(this.root, ".git", "config"),
                "[extensions]\nworktreeConfig = true\n[credential \"https://example.com/org\"]\nhelper =\nhelper = wincred\n");
            File.WriteAllText(Path.Combine(this.root, ".git", "config.worktree"), "[credential]\nhelper = store\n");

            EventMetadata snapshot = this.Collect("https://example.com/org/repo", startInfo =>
            {
                startInfo.Environment["GIT_CONFIG_COUNT"] = "2";
                startInfo.Environment["GIT_CONFIG_KEY_0"] = "credential.helper";
                startInfo.Environment["GIT_CONFIG_VALUE_0"] = string.Empty;
                startInfo.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
                startInfo.Environment["GIT_CONFIG_VALUE_1"] = "!echo " + Secret;
            });

            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("omitted:shell"));
            string expectedOperations = string.Join(", ", new[]
            {
                "global:manager", "global:cache", "local:reset", "local:wincred", "worktree:store", "command:reset", "command:omitted:shell",
            });
            using (JsonDocument payload = JsonDocument.Parse(EventMetadataConverter.SerializeToString(snapshot)))
            {
                Assert.That(payload.RootElement.GetProperty("HelperChain").GetString(), Is.EqualTo("omitted:shell"));
                Assert.That(payload.RootElement.GetProperty("HelperOperations").GetString(), Is.EqualTo(expectedOperations));
            }

            this.AssertNoSecret(snapshot);
        }

        [TestCase("", "absent")]
        [TestCase("[credential]\nhelper = manager\nhelper =\n", "reset-empty")]
        public void AbsenceAndResetAreDistinct(string config, string state)
        {
            File.WriteAllText(Path.Combine(this.root, "global"), config);
            EventMetadata snapshot = this.Collect("https://example.com/org");
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperState"], Is.EqualTo(state));
            Assert.That(snapshot["HelperChain"], Is.Empty);
        }

        [Test]
        public void ConfigReadErrorsDoNotProduceAnEmptySuccessfulSnapshot()
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[invalid\n" + Secret);
            int reads = 0;
            MockTracer tracer = new MockTracer();
            EventMetadata snapshot = this.Collect("https://example.com/org", info => ++reads, tracer: tracer);
            Assert.That(snapshot["Status"], Is.EqualTo("config-query-error"));
            Assert.That(snapshot.ContainsKey("HelperChain"), Is.False);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(tracer.RelatedWarningEvents, Is.Empty);
            this.AssertNoSecret(snapshot);
        }

        [TestCase(AuthenticationConfiguration.ReadCommand, 1, 2, "collected", 1)]
        [TestCase("config --system --null --name-only --list", 1, 2, "collected", 1)]
        [TestCase("config --file - --no-includes --null --get-urlmatch authdiagnostic", 1, 2, "collected", 1)]
        [TestCase(AuthenticationConfiguration.ReadCommand, 5, 5, "config-query-error", 5)]
        public void DiagnosticReadsUseSharedRetriesWithoutTracingConfiguration(
            string query, int failures, int expectedAttempts, string expectedStatus, int expectedWarnings)
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential \"https://example.com/org\"]\nhelper = manager\n");
            string systemConfig = Path.Combine(this.root, "system");
            File.WriteAllText(systemConfig, string.Empty);
            int attempts = 0;
            MockTracer tracer = new MockTracer();

            EventMetadata snapshot = this.Collect("https://example.com/org", info =>
            {
                info.Environment.Remove("GIT_CONFIG_NOSYSTEM");
                info.Environment["GIT_CONFIG_SYSTEM"] = systemConfig;
                if (info.Arguments.Contains(query, StringComparison.Ordinal) && ++attempts <= failures)
                {
                    info.FileName = Environment.GetEnvironmentVariable("ComSpec");
                    info.Arguments = "/d /c \"findstr . >nul & echo warning: unable to access '" + Secret + "': Permission denied 1>&2 & exit /b 128\"";
                }
            }, tracer: tracer);

            Assert.That(attempts, Is.EqualTo(expectedAttempts));
            Assert.That(snapshot["Status"], Is.EqualTo(expectedStatus));
            Assert.That(tracer.TelemetryWarnings.Count, Is.EqualTo(expectedWarnings));
            Assert.That(string.Join("\n", tracer.RelatedWarningEvents), Does.Not.Contain(Secret));
            if (expectedStatus == "collected")
            {
                Assert.That(snapshot["HelperChain"], Is.EqualTo("manager"));
                Assert.That(snapshot["SystemConfigState"], Is.EqualTo("readable-no-auth-settings"));
            }
            else
            {
                Assert.That(snapshot.ContainsKey("HelperChain"), Is.False);
            }

            this.AssertNoSecret(snapshot);
        }

        [Test]
        public void OversizedConfigDoesNotProduceAPartialHelperChain()
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential]\nhelper = " + new string('x', 70000));
            EventMetadata snapshot = this.Collect("https://example.com/org");
            Assert.That(snapshot["Status"], Is.EqualTo("query-output-limit"));
            Assert.That(snapshot.ContainsKey("HelperChain"), Is.False);
        }

        [Test]
        public void SystemConfigAbsenceIsReportedSeparately()
        {
            EventMetadata snapshot = this.Collect("https://example.com/org", info =>
            {
                info.Environment.Remove("GIT_CONFIG_NOSYSTEM");
                info.Environment["GIT_CONFIG_SYSTEM"] = Path.Combine(this.root, "missing-system-config");
                info.Environment["LC_ALL"] = "fr_FR.UTF-8";
            });
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperState"], Is.EqualTo("absent"));
            Assert.That(snapshot["SystemConfigState"], Is.EqualTo("missing"));
        }

        [Test]
        public void CommandOverridesCannotInjectSyntheticMatcherKeys()
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential \"https://other.example.com\"]\nhelper = manager\n");
            EventMetadata snapshot = this.Collect("https://example.com/org", info =>
            {
                info.Environment["GIT_CONFIG_COUNT"] = "1";
                info.Environment["GIT_CONFIG_KEY_0"] = "authdiagnostic.entry0";
                info.Environment["GIT_CONFIG_VALUE_0"] = "true";
            });
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperState"], Is.EqualTo("absent"));
        }

        [Test]
        public void InvalidAndValuelessPreferencesAreNotReportedAsSafeValues()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nhelper\nuseHttpPath = " + Secret + "\nmsauthUseBroker = " + Secret + "\nazreposCredentialType = " + Secret + "\n");
            EventMetadata snapshot = this.Collect("https://example.com/org");
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("omitted:valueless"));
            Assert.That(snapshot["CredentialConfigState"], Is.EqualTo("invalid-valueless-setting"));
            Assert.That(snapshot["UseHttpPath"], Is.EqualTo("omitted:invalid"));
            Assert.That(snapshot["Gcm_msauthusebroker"], Is.EqualTo("unavailable"));
            Assert.That(snapshot["Gcm_azreposcredentialtype"], Is.EqualTo("unavailable"));
            this.AssertNoSecret(snapshot);
        }

        [Test]
        public void GcmInvalidPreferencesAreOmittedWithoutDefaultingToSafeValues()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nmsauthUseBroker = " + Secret + "\nazreposCredentialType = " + Secret + "\ninteractive = NEVER\n");
            EventMetadata snapshot = this.Collect("https://example.com/org");
            Assert.That(snapshot["Gcm_msauthusebroker"], Is.EqualTo("omitted:invalid"));
            Assert.That(snapshot["Gcm_azreposcredentialtype"], Is.EqualTo("omitted:invalid"));
            Assert.That(snapshot["Gcm_interactive"], Is.EqualTo("never"));
            Assert.That(snapshot["GitInteractive"], Is.EqualTo("omitted:invalid"), "Git's special 'never' value is case-sensitive.");
            this.AssertNoSecret(snapshot);
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticQueriesHaveOneTimeoutBudgetAndStopTheirOwnedProcess()
        {
            using ManualResetEventSlim exited = new ManualResetEventSlim();
            Stopwatch timer = Stopwatch.StartNew();
            int attempts = 0;
            MockTracer tracer = new MockTracer();
            EventMetadata snapshot = AuthenticationConfiguration.Collect("https://example.com/org", command =>
            {
                ++attempts;
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec"),
                        attempts == 1 ?
                            "/d /c \"echo warning: unable to access 'config': Permission denied 1>&2 & exit /b 128\"" :
                            "/d /c ping -n 30 127.0.0.1 >nul")
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true,
                };
                if (attempts > 1)
                {
                    process.Exited += (sender, args) => exited.Set();
                }

                return process;
            }, tracer: tracer);
            Assert.That(snapshot["Status"], Is.EqualTo("query-timeout"));
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(tracer.TelemetryWarnings.Count, Is.EqualTo(1));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(8)));
            Assert.That(exited.Wait(TimeSpan.FromSeconds(1)), Is.True);
        }

        [Test]
        public void CredentialFillCommandScopedUseHttpPathOverrideIsCaptured()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nuseHttpPath = false\n[credential \"https://dev.azure.com/org\"]\nazreposCredentialType = oauth\n");
            EventMetadata snapshot = this.Collect("https://dev.azure.com/org/repo", info =>
            {
                info.Arguments = "-c credential.\"https://dev.azure.com\".useHttpPath=true " + info.Arguments;
            });
            Assert.That(snapshot["UseHttpPath"], Is.EqualTo("true"));
            Assert.That(snapshot["Gcm_azreposcredentialtype"], Is.EqualTo("oauth"));
        }

        [Test]
        public void DiagnosticsReuseTheFailedCredentialProcessEnvironment()
        {
            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                { "SystemRoot", Environment.GetEnvironmentVariable("SystemRoot") },
                { "GIT_CONFIG_NOSYSTEM", "1" },
                { "GIT_CONFIG_GLOBAL", Path.Combine(this.root, "global") },
                { "GIT_CONFIG_COUNT", "1" },
                { "GIT_CONFIG_KEY_0", "credential.helper" },
                { "GIT_CONFIG_VALUE_0", "manager-core" },
                { "GCM_MSAUTH_USEBROKER", "true" },
            };
            EventMetadata snapshot = this.Collect(
                "https://example.com/org",
                info => info.Environment["GCM_MSAUTH_USEBROKER"] = "false",
                environment);
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["EnvironmentTiming"], Is.EqualTo("credential-fill-process"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("manager-core"));
            Assert.That(snapshot["Gcm_msauthusebroker"], Is.EqualTo("true"));
        }

        [Test]
        public void GcmPreferencesUsePathParentHostAndEnvironmentButGitInteractiveIsUnscoped()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\ninteractive = never\nmsauthUseBroker = false\n" +
                "[credential \"example.com\"]\nazreposCredentialType = pat\n" +
                "[credential \"https://child.example.com/org\"]\nazreposCredentialType = oauth\ninteractive = true\n" +
                "[credential \"https://child.example.com\"]\nuseHttpPath = true\n");
            EventMetadata snapshot = this.Collect("https://child.example.com/org/repo");
            Assert.That(snapshot["Gcm_azreposcredentialtype"], Is.EqualTo("oauth"));
            Assert.That(snapshot["Gcm_msauthusebroker"], Is.EqualTo("false"));
            Assert.That(snapshot["Gcm_interactive"], Is.EqualTo("true"));
            Assert.That(snapshot["GitInteractive"], Is.EqualTo("never"));

            EventMetadata inherited = this.Collect("https://child.example.com/other", info =>
            {
                info.Environment["GCM_MSAUTH_USEBROKER"] = "true";
                info.Environment["GCM_INTERACTIVE"] = "never";
            });
            Assert.That(inherited["Gcm_azreposcredentialtype"], Is.EqualTo("pat"));
            Assert.That(inherited["Gcm_msauthusebroker"], Is.EqualTo("true"));
            Assert.That(inherited["Gcm_msauthusebroker_Source"], Is.EqualTo("environment"));
            Assert.That(inherited["Gcm_interactive"], Is.EqualTo("never"));
        }

        [Test]
        public void UnsafeValuesAndUrlPasswordsNeverEnterLocalOrRemoteMetadata()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential \"https://example.com/" + Secret + "\"]\nhelper = !echo " + Secret + "\n" +
                "azreposCredentialType = " + Secret + "\nmsauthUseBroker = " + Secret + "\n");
            EventMetadata snapshot = this.Collect("https://user:" + Secret + "@example.com/" + Secret, info =>
            {
                info.Environment["GCM_INTERACTIVE"] = Secret;
                info.Environment["GIT_TRACE2_EVENT"] = Path.Combine(this.root, "trace");
            });
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["Gcm_interactive"], Is.EqualTo("omitted:invalid"));
            Assert.That(snapshot["Gcm_azreposcredentialtype"], Is.EqualTo("unset"), "GCM receives no path when useHttpPath is false.");
            Assert.That(snapshot["HelperChain"], Is.EqualTo("omitted:shell"));
            Assert.That(File.Exists(Path.Combine(this.root, "trace")), Is.False);
            this.AssertNoSecret(snapshot);
        }

        [TestCase("example.com", "https://example.com/org", true)]
        [TestCase("/org", "https://example.com/org", true)]
        [TestCase("/org", "https://example.com/org/repo", false)]
        [TestCase("https://", "https://example.com/org", true)]
        [TestCase("example.com", "https://other.example.com/org", false)]
        public void CredentialPartialUrlFallbackUsesExactComponents(string scope, string url, bool applies)
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential \"" + scope + "\"]\nhelper = manager\n");
            EventMetadata snapshot = this.Collect(url);
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo(applies ? "manager" : string.Empty));
        }

        [Test]
        public void OnlyFailedAttemptsCollectAndRepeatedFailuresAreThrottled()
        {
            RecordingGitProcess git = new RecordingGitProcess();
            int fills = 0;
            git.SetExpectedCommandResult(
                "-c credential.\"https://dev.azure.com\".useHttpPath=true credential fill",
                () => ++fills == 1 ? new GitProcess.Result("password=token\n", string.Empty, 0) : new GitProcess.Result(string.Empty, Secret, 128));
            MockTracer tracer = new MockTracer();
            Assert.That(git.TryGetCredential(tracer, "mock://repo", out _, out _, out _), Is.True);
            Assert.That(git.Collections, Is.Zero);
            Assert.That(git.TryGetCredential(tracer, "mock://repo", out _, out _, out _), Is.False);
            Assert.That(git.TryGetCredential(tracer, "mock://repo", out _, out _, out _), Is.False);
            Assert.That(git.Collections, Is.EqualTo(1));
            Assert.That(tracer.RelatedEventNames.Count(n => n == "AuthenticationConfiguration"), Is.EqualTo(1));
            int index = tracer.RelatedEventNames.IndexOf("AuthenticationConfiguration");
            Assert.That(tracer.RelatedEventKeywords[index], Is.EqualTo(Keywords.Network | Keywords.Telemetry));
        }

        [Test]
        public void ValidEncodedSeparatorScopeDoesNotApplyItsResetThroughPartialFallback()
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nhelper = manager\n[credential \"https://example.com/team%2Frepo\"]\nhelper =\n");
            EventMetadata snapshot = this.Collect("https://example.com/team/repo");
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("manager"));
            Assert.That(snapshot["HelperOperations"], Is.EqualTo("global:manager"));
        }

        [TestCase("https://example.com/%zz", "https://example.com/%25zz")]
        [TestCase("https://user:pass%zz@example.com/org", "https://user@example.com/org/repo")]
        public void NonNormalizableFullUrlScopeReportsAnUnsupportedStateInsteadOfGuessing(string scope, string url)
        {
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential \"" + scope + "\"]\nhelper = manager\n");
            EventMetadata snapshot = this.Collect(url);
            Assert.That(snapshot["Status"], Is.EqualTo("unsupported-config-url"));
            Assert.That(snapshot.ContainsKey("HelperChain"), Is.False);
        }

        [Test]
        public void UsernameTokensRetainMatchingWithoutEnteringProcessArgumentsOrTrace2()
        {
            string trace = Path.Combine(this.root, "trace");
            List<ProcessStartInfo> processes = new List<ProcessStartInfo>();
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[trace2]\neventTarget = " + trace.Replace('\\', '/') + "\n" +
                "[credential \"https://" + Secret + ":password@example.com/org\"]\nhelper = manager\n" +
                "[credential \"https://other@example.com/org\"]\nhelper = store\n");
            EventMetadata snapshot = this.Collect("https://" + Secret + "@example.com/org", processes.Add);
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("manager"));
            Assert.That(processes.All(info => !info.Arguments.Contains(Secret, StringComparison.Ordinal)), Is.True);
            Assert.That(File.Exists(trace), Is.False);
            Assert.That(AuthenticationConfiguration.CredentialUrl.TryParse("https://" + Secret + "@example.com/org", out var url), Is.True);
            Assert.That(url.Format(redactUsername: true), Is.EqualTo("https://authdiagnostic-user@example.com/org"));
            this.AssertNoSecret(snapshot);
        }

        [TestCase("https://example.com/org?token=" + Secret)]
        [TestCase("https://example.com/org#token=" + Secret)]
        [TestCase("https://user:p@ss@" + Secret + "/org")]
        public void SensitiveRepositoryUrlsReportAnExplicitUnsupportedState(string url)
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential \"https://example.com\"]\nhelper = manager\n");
            EventMetadata snapshot = this.Collect(url);
            Assert.That(snapshot["Status"], Is.EqualTo("unsupported-sensitive-repository-url"));
            Assert.That(snapshot.ContainsKey("HelperChain"), Is.False);
            this.AssertNoSecret(snapshot);
        }

        [Test]
        public void ConfigCommandOnlyRedirectDoesNotReplaceTheCredentialConfiguration()
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential]\nhelper = manager\n");
            string decoy = Path.Combine(this.root, "decoy");
            File.WriteAllText(decoy, "[credential]\nhelper = store\n");
            EventMetadata snapshot = this.Collect("https://example.com/org", info => info.Environment["GIT_CONFIG"] = decoy);
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["HelperChain"], Is.EqualTo("manager"));
        }

        [Test]
        public void EmptyGcmInteractiveAllowsInteractionButGitBooleanIsFalse()
        {
            File.WriteAllText(Path.Combine(this.root, "global"), "[credential]\ninteractive =\n");
            EventMetadata snapshot = this.Collect("https://example.com/org");
            Assert.That(snapshot["GitInteractive"], Is.EqualTo("false"));
            Assert.That(snapshot["Gcm_interactive"], Is.EqualTo("true"));
            snapshot = this.Collect("https://example.com/org", info => info.Environment["GCM_INTERACTIVE"] = string.Empty);
            Assert.That(snapshot["Gcm_interactive"], Is.EqualTo("true"));
            Assert.That(snapshot["Gcm_interactive_Source"], Is.EqualTo("environment"));
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TreeKillFailuresCannotEscapeTheCancellationCallback()
        {
            Assert.That(GitProcessConfigReader.TryKillProcess(() => throw new AggregateException(new System.ComponentModel.Win32Exception())), Is.False);
            Assert.That(GitProcessConfigReader.TryKillProcess(() => throw new System.ComponentModel.Win32Exception()), Is.False);
            Assert.That(GitProcessConfigReader.TryKillProcess(() => throw new InvalidOperationException()), Is.True);
        }

        [Test]
        public void FailedProcessStartReportsOnlyItsStatus()
        {
            EventMetadata snapshot = AuthenticationConfiguration.Collect("https://example.com/org", command =>
                new Process { StartInfo = new ProcessStartInfo(Path.Combine(this.root, Secret + ".exe")) });
            Assert.That(snapshot["Status"], Is.EqualTo("query-process-error"));
            this.AssertNoSecret(snapshot);
        }

        [Test]
        public void MalformedAndExcessConfigEntriesAreRejected()
        {
            Assert.That(AuthenticationConfiguration.TryParseEntries("global\0credential.helper\nmanager", out _), Is.False);
            string entries = string.Concat(Enumerable.Repeat("global\0credential.helper\nmanager\0", 129));
            Assert.That(AuthenticationConfiguration.TryParseEntries(entries, out _), Is.False);
        }

        [Test]
        public void ExitZeroWithoutPasswordCollectsButCertificateFailureDoesNot()
        {
            RecordingGitProcess git = new RecordingGitProcess();
            git.SetExpectedCommandResult(
                "-c credential.\"https://dev.azure.com\".useHttpPath=true credential fill",
                () => new GitProcess.Result("username=someone\n", string.Empty, 0));
            MockTracer tracer = new MockTracer();
            Assert.That(git.TryGetCredential(tracer, "mock://repo", out _, out _, out _), Is.False);
            Assert.That(git.Collections, Is.EqualTo(1));
            Assert.That(tracer.RelatedEventNames.Count(n => n == "AuthenticationConfiguration"), Is.EqualTo(1));

            git = new RecordingGitProcess();
            git.SetExpectedCommandResult("credential fill", () => new GitProcess.Result(string.Empty, string.Empty, 128));
            Assert.That(git.TryGetCertificatePassword(new MockTracer(), "certificate-path", out _, out _), Is.False);
            Assert.That(git.Collections, Is.Zero);
        }

        [Test]
        public void ProductionFillCarriesItsOwnEnvironmentIntoTheCollector()
        {
            File.AppendAllText(Path.Combine(this.root, ".git", "config"),
                "[credential]\nhelper =\ninteractive = never\n");
            PlumbingGitProcess git = new PlumbingGitProcess(this.GetGitPath(), this.root);
            MockTracer tracer = new MockTracer();
            Assert.That(git.TryGetCredential(tracer, "https://dev.azure.com/org/repo", out _, out _, out _, timeoutMs: 5000), Is.False);
            Assert.That(git.CollectedEnvironment, Is.SameAs(git.ResultEnvironment));
            Assert.That(git.CollectedEnvironment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
            Assert.That(git.CollectedEnvironment["GCM_VALIDATE"], Is.EqualTo("0"));
            EventMetadata snapshot = tracer.RelatedEventMetadata[tracer.RelatedEventNames.IndexOf("AuthenticationConfiguration")];
            Assert.That(snapshot["Status"], Is.EqualTo("collected"));
            Assert.That(snapshot["EnvironmentTiming"], Is.EqualTo("credential-fill-process"));
            Assert.That(snapshot["UseHttpPath"], Is.EqualTo("true"));
            Assert.That(snapshot["HelperState"], Is.EqualTo("reset-empty"));
        }

        [Test]
        public void FailedCredentialFillBeforeCloneCreatesTheWorkingDirectoryCollectsConfiguration()
        {
            string workingDirectory = Path.Combine(this.root, "enlistment", "src");
            string systemConfig = Path.Combine(this.root, "system");
            File.WriteAllText(systemConfig, string.Empty);
            File.WriteAllText(Path.Combine(this.root, "global"),
                "[credential]\nhelper = manager\n" +
                "[credential \"https://dev.azure.com/org\"]\nhelper =\nhelper = store --file=" + Secret + "\n");
            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                { "SystemRoot", Environment.GetEnvironmentVariable("SystemRoot") },
                { "GIT_CONFIG_SYSTEM", systemConfig },
                { "GIT_CONFIG_GLOBAL", Path.Combine(this.root, "global") },
                { "GIT_TERMINAL_PROMPT", "0" },
                { "GCM_VALIDATE", "0" },
            };
            PreCloneGitProcess git = new PreCloneGitProcess(this.GetGitPath(), workingDirectory, environment);
            MockTracer tracer = new MockTracer();

            Assert.That(git.TryGetCredential(tracer, "https://dev.azure.com/org/repo", out _, out _, out _), Is.False);
            Assert.That(Directory.Exists(workingDirectory), Is.False);
            EventMetadata snapshot = tracer.RelatedEventMetadata[tracer.RelatedEventNames.IndexOf("AuthenticationConfiguration")];
            using (JsonDocument payload = JsonDocument.Parse(EventMetadataConverter.SerializeToString(snapshot)))
            {
                Assert.That(payload.RootElement.GetProperty("Status").GetString(), Is.EqualTo("collected"));
                Assert.That(payload.RootElement.GetProperty("EnvironmentTiming").GetString(), Is.EqualTo("credential-fill-process"));
                Assert.That(payload.RootElement.GetProperty("HelperChain").GetString(), Is.EqualTo("store:omitted-arguments"));
                Assert.That(payload.RootElement.GetProperty("HelperOperations").GetString(),
                    Is.EqualTo("global:manager, global:reset, global:store:omitted-arguments"));
                Assert.That(payload.RootElement.GetProperty("SystemConfigState").GetString(), Is.EqualTo("readable-no-auth-settings"));
                Assert.That(payload.RootElement.GetProperty("UseHttpPath").GetString(), Is.EqualTo("true"));
            }

            this.AssertNoSecret(snapshot);
        }

        private string GetGitPath()
        {
            if (!File.Exists(this.gitPath))
            {
                Assert.Fail("The authentication configuration integration tests require Git on PATH.");
            }

            return this.gitPath;
        }

        private EventMetadata Collect(
            string url,
            Action<ProcessStartInfo> customize = null,
            Dictionary<string, string> credentialFillEnvironment = null,
            ITracer tracer = null)
        {
            GitProcess git = new GitProcess(this.GetGitPath(), this.root);
            return AuthenticationConfiguration.Collect(url, command =>
            {
                Process process = git.GetGitProcess(command, this.root, Path.Combine(this.root, ".git"), false, null, false);
                foreach (string key in process.StartInfo.Environment.Keys.Where(k =>
                    k.StartsWith("GIT_CONFIG", StringComparison.OrdinalIgnoreCase) || k.StartsWith("GCM_", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    process.StartInfo.Environment.Remove(key);
                }

                process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(this.root, "global");
                customize?.Invoke(process.StartInfo);
                return process;
            }, credentialFillEnvironment, tracer);
        }

        private void AssertNoSecret(EventMetadata metadata)
        {
            Assert.That(EventMetadataConverter.SerializeToString(metadata), Does.Not.Contain(Secret).And.Not.Contain("System.String[]"));
        }

        private sealed class RecordingGitProcess : MockGitProcess
        {
            internal int Collections { get; private set; }

            protected override EventMetadata GetAuthenticationConfiguration(string repoUrl, Dictionary<string, string> credentialFillEnvironment, ITracer tracer)
            {
                ++this.Collections;
                return new EventMetadata { { "Status", "config-query-error" } };
            }
        }

        private sealed class PreCloneGitProcess : GitProcess
        {
            private readonly Dictionary<string, string> environment;

            internal PreCloneGitProcess(string gitPath, string root, Dictionary<string, string> environment)
                : base(gitPath, root)
            {
                this.environment = environment;
            }

            protected override Result InvokeGitImpl(
                string command,
                string workingDirectory,
                string dotGitDirectory,
                bool useReadObjectHook,
                Action<StreamWriter> writeStdIn,
                Action<string> parseStdOutLine,
                int timeoutMs,
                string gitObjectsDirectory = null,
                bool usePreCommandHook = true,
                Action<string> parseStdOutToken = null,
                bool mayRequireAuth = false,
                bool? hasVisibleConsoleWindow = null)
            {
                Assert.That(command, Does.EndWith("credential fill"));
                return new Result(string.Empty, "Credential fill failed", 128)
                {
                    CredentialFillEnvironment = this.environment,
                };
            }
        }

        private sealed class PlumbingGitProcess : GitProcess
        {
            internal PlumbingGitProcess(string gitPath, string root)
                : base(gitPath, root)
            {
            }

            internal Dictionary<string, string> ResultEnvironment { get; private set; }
            internal Dictionary<string, string> CollectedEnvironment { get; private set; }

            protected override EventMetadata GetAuthenticationConfiguration(string repoUrl, Dictionary<string, string> credentialFillEnvironment, ITracer tracer)
            {
                this.CollectedEnvironment = credentialFillEnvironment;
                return base.GetAuthenticationConfiguration(repoUrl, credentialFillEnvironment, tracer);
            }

            protected override Result InvokeGitImpl(
                string command,
                string workingDirectory,
                string dotGitDirectory,
                bool useReadObjectHook,
                Action<StreamWriter> writeStdIn,
                Action<string> parseStdOutLine,
                int timeoutMs,
                string gitObjectsDirectory = null,
                bool usePreCommandHook = true,
                Action<string> parseStdOutToken = null,
                bool mayRequireAuth = false,
                bool? hasVisibleConsoleWindow = null)
            {
                Result result = base.InvokeGitImpl(command, workingDirectory, dotGitDirectory, useReadObjectHook, writeStdIn,
                    parseStdOutLine, timeoutMs, gitObjectsDirectory, usePreCommandHook, parseStdOutToken, mayRequireAuth, hasVisibleConsoleWindow);
                this.ResultEnvironment = result.CredentialFillEnvironment;
                return result;
            }
        }
    }
}
