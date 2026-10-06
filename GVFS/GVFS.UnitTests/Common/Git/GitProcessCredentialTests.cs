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
        }

        [TestCase("", "")]
        [TestCase("protocol=https\r\nhost=example.com\r\nusername=someone\r\npassword=" + SecretValue, "protocol,host,username,password")]
        [TestCase("protocol=https\rhost=example.com\nusername=someone\npassword=" + SecretValue, "protocol,host,username,password")]
        [TestCase("no separator " + SecretValue + "\n=" + SecretValue + "\nusername=someone\npassword=" + SecretValue, "<malformed>,<malformed>,username,password")]
        [TestCase("protocol=https\nhost=example.com\nurl=https://example.com\nwwwauth[]=Bearer realm=" + SecretValue + "\ncapability[]=authtype\noauth_refresh_token=" + SecretValue + "\nusername=someone\npassword=" + SecretValue, "protocol,host,url,<other>,<other>,<other>,username,password")]
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

        private static EventMetadata GetActivityMetadata(MockTracer tracer)
        {
            MockTracer activityTracer = tracer.StartActivityTracer;
            activityTracer.ShouldNotBeNull("The credential call must start an activity");
            activityTracer.StoppedActivityMetadata.Count.ShouldEqual(1);

            return activityTracer.StoppedActivityMetadata.Single();
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
