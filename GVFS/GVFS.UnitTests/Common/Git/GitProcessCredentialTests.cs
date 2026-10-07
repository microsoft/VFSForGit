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

        [TestCase]
        public void TryGetCredentialDoesNotTraceSecretWhenParseFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();

            // The secret is on the last line and has no terminating newline, so the parse fails.
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    "protocol=https\nhost=example.com\nhelper output " + SecretValue + "=ignored\nusername=someone\npassword=" + SecretValue,
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _)
                .ShouldBeFalse("Parse of the credential output must fail for this test");

            EventMetadata metadata = GetActivityMetadata(tracer);
            AssertNoSecret(metadata);
            metadata["OutputKeys"].ShouldEqual("protocol,host,<other>,username,password");
            AssertNoSecret(tracer);
        }

        [TestCase]
        public void TryGetCertificatePasswordDoesNotTraceSecretWhenParseFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();

            // The secret is on the last line and has no terminating newline, so the parse fails.
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result(
                    "protocol=cert\npath=mock://certificate\npassword=" + SecretValue,
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out _, out _)
                .ShouldBeFalse("Parse of the credential output must fail for this test");

            EventMetadata metadata = GetActivityMetadata(tracer);
            AssertNoSecret(metadata);
            metadata["OutputKeys"].ShouldEqual("protocol,path,password");
            AssertNoSecret(tracer);
        }

        [TestCase("", "")]
        [TestCase("protocol=https\r\nhost=example.com\r\nusername=someone\r\npassword=" + SecretValue, "protocol,host,username,password")]
        [TestCase("protocol=https\rhost=example.com\nusername=someone\npassword=" + SecretValue, "protocol,host,username,password")]
        [TestCase("no separator " + SecretValue + "\n=" + SecretValue + "\nusername=someone\npassword=" + SecretValue, "<malformed>,<malformed>,username,password")]
        [TestCase("protocol=https\nhost=example.com\nurl=https://example.com\nwwwauth[]=Bearer realm=" + SecretValue + "\ncapability[]=authtype\noauth_refresh_token=" + SecretValue + "\nusername=someone\npassword=" + SecretValue, "protocol,host,url,wwwauth[],capability[],oauth_refresh_token,username,password")]
        [TestCase("protocol=https\n password=" + SecretValue + "\nPassword=" + SecretValue + "\npassword =" + SecretValue + "\nusername=someone\npassword=" + SecretValue, "protocol,<other>,<other>,<other>,username,password")]
        [TestCase("custom_" + SecretValue + "=ignored\nusername=someone\npassword=" + SecretValue, "<other>,username,password")]
        public void TryGetCredentialTracesOnlyKnownKeyNamesWhenParseFails(string output, string expectedKeys)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(output, string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _)
                .ShouldBeFalse("Parse of the credential output must fail for this test");

            EventMetadata metadata = GetActivityMetadata(tracer);
            AssertNoSecret(metadata);
            metadata["OutputKeys"].ShouldEqual(expectedKeys);
            AssertNoSecret(tracer);
        }

        [TestCase]
        public void TryGetCredentialDoesNotTraceOutputKeysWhenParseSucceeds()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    "protocol=https\nhost=example.com\nusername=someone\npassword=" + SecretValue + "\n",
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeTrue();

            EventMetadata metadata = GetActivityMetadata(tracer);
            metadata.ContainsKey("OutputKeys").ShouldBeFalse("Output keys are only traced when the parse fails");
            AssertNoSecret(metadata);
        }

        [TestCase]
        public void TryGetCredentialAcceptsMissingUsername()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    "protocol=https\nhost=example.com\npassword=" + SecretValue + "\n",
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out _).ShouldBeTrue();

            username.ShouldEqual(string.Empty);
            password.ShouldEqual(SecretValue);
            GetActivityMetadata(tracer).ContainsKey("OutputKeys").ShouldBeFalse("Output keys are only traced when the parse fails");
        }

        [TestCase]
        public void TryGetCredentialFailsWhenPasswordKeyIsAbsent()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    "protocol=https\nhost=example.com\nusername=someone\n",
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out string password, out _).ShouldBeFalse();

            password.ShouldBeNull();
            GetActivityMetadata(tracer)["OutputKeys"].ShouldEqual("protocol,host,username");
        }

        [TestCase]
        public void TryGetCertificatePasswordFailsWhenPasswordKeyIsAbsent()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result(
                    "protocol=cert\npath=mock://certificate\n",
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out string password, out _).ShouldBeFalse();

            password.ShouldBeNull();
            GetActivityMetadata(tracer)["OutputKeys"].ShouldEqual("protocol,path");
        }

        [TestCase]
        public void TryGetCredentialLimitsTheNumberOfTracedKeys()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    string.Concat(Enumerable.Repeat("key=value\n", 100)) + "password=" + SecretValue,
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            string expectedKeys = string.Join(",", Enumerable.Repeat("<other>", 32)) + ",...";
            GetActivityMetadata(tracer)["OutputKeys"].ShouldEqual(expectedKeys);
        }

        [TestCase]
        public void TryGetCredentialDoesNotTraceKeysWhenOutputIsTruncated()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result("protocol=https\nhost=example.com\n", string.Empty, GitProcess.Result.SuccessCode, outputTruncated: true, errorsTruncated: false));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _).ShouldBeFalse();

            GetActivityMetadata(tracer)["OutputKeys"].ShouldEqual("<truncated>");
        }

        [TestCase]
        public void TryGetCredentialReportsWhyItFailedWhenThePasswordIsMissing()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result("protocol=https\nusername=someone\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out string errorMessage).ShouldBeFalse();

            errorMessage.ShouldEqual("Git credential fill returned no password");
        }

        [TestCase]
        public void TryGetCertificatePasswordReportsWhyItFailedWhenThePasswordIsMissing()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result("protocol=cert\npath=mock://certificate\n", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out _, out string errorMessage).ShouldBeFalse();

            errorMessage.ShouldEqual("Git credential fill returned no password");
        }

        [TestCase]
        public void TryGetCertificatePasswordDoesNotTraceOutputKeysWhenParseSucceeds()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result(
                    "protocol=cert\npath=mock://certificate\npassword=" + SecretValue + "\n",
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out string password, out _).ShouldBeTrue();

            password.ShouldEqual(SecretValue);
            EventMetadata metadata = GetActivityMetadata(tracer);
            metadata.ContainsKey("OutputKeys").ShouldBeFalse("Output keys are only traced when the parse fails");
        }

        [TestCase]
        public void TryGetCredentialDoesNotSendStderrToTelemetryWhenGitFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(
                    string.Empty,
                    "warning: invalid credential line: " + SecretValue,
                    GitProcess.Result.GenericFailureCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out string errorMessage).ShouldBeFalse();

            AssertStderrIsOnlyLocal(tracer);
            errorMessage.Contains(SecretValue).ShouldBeTrue("The caller still receives the error text");
        }

        [TestCase]
        public void TryGetCertificatePasswordDoesNotSendStderrToTelemetryWhenGitFails()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "credential fill",
                () => new GitProcess.Result(
                    string.Empty,
                    "warning: invalid credential line: " + SecretValue,
                    GitProcess.Result.GenericFailureCode));

            gitProcess.TryGetCertificatePassword(tracer, "mock://certificate", out _, out _).ShouldBeFalse();

            AssertStderrIsOnlyLocal(tracer);
        }

        [TestCase("protocol=https\nxpassword=WRONG\npath=contains-password=WRONG\nusername=someone\npassword=RIGHT\n", "someone", "RIGHT")]
        [TestCase("username=bob\nurl=https://example.com/?username=evil\npassword=RIGHT\n", "bob", "RIGHT")]
        [TestCase("username=\npassword=RIGHT\n", "", "RIGHT")]
        [TestCase("password=RIGHT\r\nusername=someone\r\n", "someone", "RIGHT")]
        public void TryGetCredentialReadsOnlyLinesThatStartWithTheKey(string output, string expectedUsername, string expectedPassword)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(output, string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out _).ShouldBeTrue();

            username.ShouldEqual(expectedUsername);
            password.ShouldEqual(expectedPassword);
        }

        [TestCase("oauth_password=WRONG\nusername=someone\n")]
        [TestCase("path=contains-password=WRONG\nusername=someone\n")]
        public void TryGetCredentialFailsWhenOnlyAnotherKeyContainsThePasswordKey(string output)
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                $"{AzureDevOpsUseHttpPathString} credential fill",
                () => new GitProcess.Result(output, string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out string password, out _).ShouldBeFalse();

            password.ShouldBeNull();
        }

        private static void AssertStderrIsOnlyLocal(MockTracer tracer)
        {
            tracer.TelemetryWarningEvents.Count.ShouldEqual(1);
            tracer.TelemetryWarningEvents.Single().Contains(SecretValue).ShouldBeFalse("Telemetry events must not carry stderr");
            tracer.TelemetryWarningEvents.Single().Contains("hasVisibleConsoleWindow").ShouldBeTrue("The console visibility diagnostic stays in telemetry");
            tracer.RelatedWarningEvents.Any(e => e.Contains(SecretValue)).ShouldBeTrue("The local log keeps stderr for diagnosis");
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
                    .Concat(source.RelatedErrorEvents)
                    .Concat(source.TelemetryWarningEvents))
                {
                    traced.Contains(SecretValue).ShouldBeFalse("Credential output must not be traced: " + traced);
                }

                foreach (EventMetadata related in source.RelatedEventMetadata.Where(m => m != null))
                {
                    AssertNoSecret(related);
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
