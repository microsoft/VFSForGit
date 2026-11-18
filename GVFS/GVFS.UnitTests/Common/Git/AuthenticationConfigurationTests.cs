using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    public class AuthenticationConfigurationTests
    {
        private const string Secret = "SecretSentinelNotForTelemetry";

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
        public void MalformedAndExcessConfigEntriesAreRejected()
        {
            Assert.That(AuthenticationConfiguration.TryParseEntries("global\0credential.helper\nmanager", out _), Is.False);
            string entries = string.Concat(Enumerable.Repeat("global\0credential.helper\nmanager\0", 129));
            Assert.That(AuthenticationConfiguration.TryParseEntries(entries, out _), Is.False);
        }

        [TestCase(false, "query-process-error")]
        [TestCase(true, "query-io-error")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticKillFailuresReportThePrimaryStatusWithoutExceptionDetails(bool readAlsoFails, string status)
        {
            Win32Exception nativeFailure = new Win32Exception(5, Secret);
            InvalidOperationException snapshotFailure = new InvalidOperationException(Secret, nativeFailure);
            AggregateException failure = readAlsoFails
                ? new AggregateException(new IOException(Secret), new AggregateException(nativeFailure, snapshotFailure))
                : new AggregateException(nativeFailure, snapshotFailure);

            EventMetadata snapshot = AuthenticationConfiguration.Collect("mock://repo", _ => throw failure);

            Assert.That(snapshot["Status"], Is.EqualTo(status));
            Assert.That(snapshot.Values.Any(value => value?.ToString().Contains(Secret, StringComparison.Ordinal) == true), Is.False);
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticFailureWithANonProcessErrorIsNotHidden()
        {
            AggregateException failure = new AggregateException(new Win32Exception(5), new InvalidOperationException());

            Assert.That(Assert.Throws<AggregateException>(() => AuthenticationConfiguration.Collect("mock://repo", _ => throw failure)), Is.SameAs(failure));
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

        private sealed class RecordingGitProcess : MockGitProcess
        {
            internal int Collections { get; private set; }

            protected override EventMetadata GetAuthenticationConfiguration(string repoUrl, Dictionary<string, string> credentialFillEnvironment, ITracer tracer)
            {
                ++this.Collections;
                return new EventMetadata { { "Status", "config-query-error" } };
            }
        }
    }
}
