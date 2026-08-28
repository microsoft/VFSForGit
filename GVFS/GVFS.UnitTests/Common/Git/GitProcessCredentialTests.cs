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
                    "protocol=https\nhost=example.com\nusername=someone\npassword=" + SecretValue,
                    string.Empty,
                    GitProcess.Result.SuccessCode));

            gitProcess.TryGetCredential(tracer, "mock://repoUrl", out _, out _, out _)
                .ShouldBeFalse("Parse of the credential output must fail for this test");

            EventMetadata metadata = GetActivityMetadata(tracer);
            AssertNoSecret(metadata);
            metadata["OutputKeys"].ShouldEqual("protocol,host,username,password");
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
