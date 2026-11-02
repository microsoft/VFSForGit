using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Linq;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    public class GitProcessCredentialTests
    {
        private const string SecretValue = "S3cretSentinelValueThatMustNotBeTraced";
        private const string AzureDevOpsUseHttpPathString = "-c credential.\"https://dev.azure.com\".useHttpPath=true";
        private const string CredentialFillCommand = AzureDevOpsUseHttpPathString + " credential fill";
        private const string CertificateFillCommand = "credential fill";
        private const string MissingPasswordMessage = "Git credential fill returned no password";
        private const int GitFatalExitCode = 128;

        [TestCase]
        public void TryGetCredentialDoesNotTraceSecretWhenThePasswordIsMissing()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CredentialFillCommand,
                "protocol=https\nhost=example.com\nhelper output " + SecretValue + "=ignored\nusername=someone\ncredential=" + SecretValue + "\noauth_refresh_token=" + SecretValue + "\n");

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out string password, out string errorMessage).ShouldBeFalse();

            password.ShouldBeNull();
            errorMessage.ShouldEqual(MissingPasswordMessage);
            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual("protocol,host,<other>,username,credential,oauth_refresh_token");
            AssertNoSecret(tracer);
        }

        [TestCase]
        public void TryGetCertificatePasswordDoesNotTraceSecretWhenThePasswordIsMissing()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CertificateFillCommand,
                "protocol=cert\npath=mock://certificate\ncredential=" + SecretValue + "\n");

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out string password, out string errorMessage).ShouldBeFalse();

            password.ShouldBeNull();
            errorMessage.ShouldEqual(MissingPasswordMessage);
            EventMetadata warning = GetTelemetryWarning(tracer);
            warning["OutputKeys"].ShouldEqual("protocol,path,credential");
            warning["CertificatePath"].ShouldEqual("mock://certificate");
            AssertNoSecret(tracer);
        }

        [TestCase("", "")]
        [TestCase("protocol=https\r\nhost=example.com\r\nusername=someone\r\ncredential=" + SecretValue, "protocol,host,username,credential")]
        [TestCase("protocol=https\rhost=example.com\nusername=someone\ncredential=" + SecretValue + "\n", "protocol,username,credential")]
        [TestCase("no separator " + SecretValue + "\n=" + SecretValue + "\nusername=someone\ncredential=" + SecretValue + "\n", "<malformed>,<malformed>,username,credential")]
        [TestCase("protocol=https\nhost=example.com\nurl=https://example.com\nwwwauth[]=Bearer realm=" + SecretValue + "\ncapability[]=authtype\noauth_refresh_token=" + SecretValue + "\nusername=someone\n", "protocol,host,url,wwwauth[],capability[],oauth_refresh_token,username")]
        [TestCase("protocol=https\n password=" + SecretValue + "\nPassword=" + SecretValue + "\npassword =" + SecretValue + "\nusername=someone\n", "protocol,<other>,<other>,<other>,username")]
        [TestCase("custom_" + SecretValue + "=ignored\nusername=someone\n", "<other>,username")]
        public void TryGetCredentialTracesOnlyKnownKeyNamesWhenThePasswordIsMissing(string output, string expectedKeys)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, output);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual(expectedKeys);
            AssertNoSecret(tracer);
        }

        [TestCase]
        public void TryGetCredentialLimitsTheNumberOfTracedKeys()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CredentialFillCommand,
                string.Concat(Enumerable.Repeat("key=value\n", 100)));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            string expectedKeys = string.Join(",", Enumerable.Repeat("<other>", 32)) + ",...";
            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual(expectedKeys);
        }

        [TestCase]
        public void TryGetCredentialDoesNotTraceKeysWhenOutputIsTruncated()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CredentialFillCommand,
                "protocol=https\nhost=example.com\n",
                outputTruncated: true);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual("<truncated>");
        }

        [TestCase]
        public void TryGetCredentialReportsTheFailureInTelemetryWhenThePasswordIsMissing()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, "protocol=https\nhost=example.com\nusername=someone\n");

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            EventMetadata warning = GetTelemetryWarning(tracer);
            warning["OutputKeys"].ShouldEqual("protocol,host,username");
            ((int)warning["ExitCode"]).ShouldEqual(GitProcess.Result.SuccessCode);
            warning.ContainsKey("hasVisibleConsoleWindow").ShouldBeTrue("The console visibility diagnostic stays with the failure");
            warning[TracingConstants.MessageKey.WarningMessage].ShouldEqual(MissingPasswordMessage);
        }

        [TestCase]
        public void TryGetCredentialDoesNotReportAFailureWhenTheParseSucceeds()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CredentialFillCommand,
                "protocol=https\nhost=example.com\nusername=someone\npassword=" + SecretValue + "\n");

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out string password, out _).ShouldBeTrue();

            password.ShouldEqual(SecretValue);
            tracer.TelemetryWarnings.ShouldBeEmpty();
            GetActivityMetadata(tracer).ContainsKey("OutputKeys").ShouldBeFalse("Output keys are only traced when the parse fails");
        }

        [TestCase]
        public void TryGetCertificatePasswordDoesNotReportAFailureWhenTheParseSucceeds()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CertificateFillCommand,
                "protocol=cert\npath=mock://certificate\npassword=" + SecretValue + "\n");

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out string password, out _).ShouldBeTrue();

            password.ShouldEqual(SecretValue);
            tracer.TelemetryWarnings.ShouldBeEmpty();
            GetActivityMetadata(tracer).ContainsKey("OutputKeys").ShouldBeFalse("Output keys are only traced when the parse fails");
        }

        [TestCase]
        public void TryGetCredentialAcceptsMissingUsername()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(
                CredentialFillCommand,
                "protocol=https\nhost=example.com\npassword=" + SecretValue + "\n");

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out _).ShouldBeTrue();

            username.ShouldEqual(string.Empty);
            password.ShouldEqual(SecretValue);
        }

        [TestCase]
        public void TryGetCredentialDescribesAFailedFillWithoutSendingStderrToTelemetry()
        {
            MockTracer tracer = new MockTracer();
            string stderr = "warning: invalid credential line: " + SecretValue;
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, string.Empty, stderr, GitFatalExitCode);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out string errorMessage).ShouldBeFalse();

            AssertStderrIsOnlyLocal(tracer, stderr);
            AssertErrorMessageHasNoStderr(errorMessage);
        }

        [TestCase]
        public void TryGetCertificatePasswordDescribesAFailedFillWithoutSendingStderrToTelemetry()
        {
            MockTracer tracer = new MockTracer();
            string stderr = "warning: invalid credential line: " + SecretValue;
            MockGitProcess gitProcess = CreateGitProcess(CertificateFillCommand, string.Empty, stderr, GitFatalExitCode);

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out _, out string errorMessage).ShouldBeFalse();

            AssertStderrIsOnlyLocal(tracer, stderr);
            GetTelemetryWarning(tracer)["CertificatePath"].ShouldEqual("mock://certificate");
            AssertErrorMessageHasNoStderr(errorMessage);
        }

        [TestCase]
        public void TryGetCredentialKeepsStderrOutOfTelemetryWhenTheCredentialManagerTimesOut()
        {
            MockTracer tracer = new MockTracer();
            string stderr = "Operation timed out after 5000ms; " + SecretValue;
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, string.Empty, stderr, GitProcess.Result.GenericFailureCode);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out string errorMessage, timeoutMs: 5000).ShouldBeFalse();

            errorMessage.ShouldEqual("Credential manager did not respond within 5 seconds");
            EventMetadata warning = GetTelemetryWarning(tracer);
            ((int)warning["ErrorsLength"]).ShouldEqual(stderr.Length);
            AssertNoSecret(warning);
            tracer.RelatedWarningEvents.Any(e => e.Contains(SecretValue)).ShouldBeTrue("The local log keeps the captured stderr of a timeout");
        }

        [TestCase]
        public void TryGetCredentialFailsWhenThePasswordIsEmpty()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, "protocol=https\nusername=someone\npassword=\n");

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out string errorMessage).ShouldBeFalse();

            errorMessage.ShouldEqual(MissingPasswordMessage);
            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual("protocol,username,password");
        }

        [TestCase]
        public void TryGetCertificatePasswordFailsWhenThePasswordIsEmpty()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CertificateFillCommand, "protocol=cert\npath=mock://certificate\npassword=\n");

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out _, out string errorMessage).ShouldBeFalse();

            errorMessage.ShouldEqual(MissingPasswordMessage);
            GetTelemetryWarning(tracer)["OutputKeys"].ShouldEqual("protocol,path,password");
        }

        [TestCase("protocol=https\nxpassword=WRONG\npath=contains-password=WRONG\nusername=someone\npassword=RIGHT\n", "someone", "RIGHT")]
        [TestCase("username=bob\nurl=https://example.com/?username=evil\npassword=RIGHT\n", "bob", "RIGHT")]
        [TestCase("username=\npassword=RIGHT\n", "", "RIGHT")]
        [TestCase("password=RIGHT\r\nusername=someone\r\n", "someone", "RIGHT")]
        [TestCase("username=first\nusername=second\npassword=WRONG\npassword=RIGHT\n", "second", "RIGHT")]
        [TestCase("username=someone\npassword=RIGHT", "someone", "RIGHT")]
        public void TryGetCredentialReadsLikeGit(string output, string expectedUsername, string expectedPassword)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, output);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out _).ShouldBeTrue();

            username.ShouldEqual(expectedUsername);
            password.ShouldEqual(expectedPassword);
        }

        [TestCase("oauth_password=WRONG\nusername=someone\n")]
        [TestCase("path=contains-password=WRONG\nusername=someone\n")]
        [TestCase("username=someone\rpassword=WRONG\n")]
        public void TryGetCredentialFailsWhenNoLineStartsWithThePasswordKey(string output)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = CreateGitProcess(CredentialFillCommand, output);

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out string password, out _).ShouldBeFalse();

            password.ShouldBeNull();
        }

        private static MockGitProcess CreateGitProcess(
            string command,
            string output,
            string errors = "",
            int exitCode = GitProcess.Result.SuccessCode,
            bool outputTruncated = false)
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                command,
                () => new GitProcess.Result(output, errors, exitCode, outputTruncated, errorsTruncated: false));
            return gitProcess;
        }

        private static void AssertErrorMessageHasNoStderr(string errorMessage)
        {
            // The caller puts this message in an exception, and the retry handler reports exceptions
            // with the telemetry keyword, so it must not carry stderr.
            errorMessage.Contains(SecretValue).ShouldBeFalse("The returned error message must not carry stderr: " + errorMessage);
            errorMessage.ShouldEqual("Git credential fill failed with exit code " + GitFatalExitCode + "; see the log for its error output");
        }

        private static void AssertStderrIsOnlyLocal(MockTracer tracer, string stderr)
        {
            EventMetadata warning = GetTelemetryWarning(tracer);
            ((int)warning["ExitCode"]).ShouldEqual(GitFatalExitCode);
            ((int)warning["ErrorsLength"]).ShouldEqual(stderr.Length);
            ((bool)warning["ErrorsTruncated"]).ShouldBeFalse();
            warning.ContainsKey("hasVisibleConsoleWindow").ShouldBeTrue("The console visibility diagnostic stays in telemetry");
            AssertNoSecret(warning);
            tracer.RelatedWarningEvents.Any(e => e.Contains(SecretValue)).ShouldBeTrue("The local log keeps stderr for diagnosis");
        }

        private static EventMetadata GetTelemetryWarning(MockTracer tracer)
        {
            tracer.TelemetryWarnings.Count.ShouldEqual(1);
            return tracer.TelemetryWarnings.Single();
        }

        private static EventMetadata GetActivityMetadata(MockTracer tracer)
        {
            MockTracer activityTracer = tracer.StartActivityTracer;
            activityTracer.ShouldNotBeNull("The credential call must start an activity");
            activityTracer.StopMetadata.Count.ShouldEqual(1);

            return activityTracer.StopMetadata.Single();
        }

        private static void AssertNoSecret(MockTracer tracer)
        {
            foreach (MockTracer source in new[] { tracer, tracer.StartActivityTracer })
            {
                foreach (string traced in source.RelatedInfoEvents
                    .Concat(source.RelatedWarningEvents)
                    .Concat(source.RelatedErrorEvents))
                {
                    traced.Contains(SecretValue).ShouldBeFalse("Credential output must not be traced: " + traced);
                }

                foreach (EventMetadata metadata in source.TelemetryWarnings
                    .Concat(source.RelatedEventMetadata)
                    .Concat(source.StopMetadata)
                    .Where(m => m != null))
                {
                    AssertNoSecret(metadata);
                }
            }
        }

        private static void AssertNoSecret(EventMetadata metadata)
        {
            foreach (object value in metadata.Values)
            {
                string text = value?.ToString() ?? string.Empty;
                text.Contains(SecretValue).ShouldBeFalse("Credential output must not be traced: " + text);
            }
        }
    }
}
