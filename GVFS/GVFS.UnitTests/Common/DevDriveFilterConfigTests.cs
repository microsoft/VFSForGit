using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.ComponentModel;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class DevDriveFilterConfigTests
    {
        [TestCase(null, null, true)]
        [TestCase(true, null, true)]
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(null, false, false)]
        [TestCase(false, null, false)]
        public void EitherVetoPreventsAnUpdateAndUnsetKeepsTheDefault(bool? system, bool? client, bool expected)
        {
            DevDriveFilterConfig.IsEnabled(
                (string key, out bool? value, out string error) =>
                {
                    key.ShouldEqual("gvfs.auto-allow-devdrive-filter");
                    value = system;
                    error = null;
                    return true;
                },
                client,
                "system",
                new MockTracer(),
                out string reason).ShouldEqual(expected);
            if (expected)
            {
                reason.ShouldBeNull();
            }
            else
            {
                reason.ShouldContain(system == false ? "system configuration" : "enlistment configuration");
            }
        }

        [TestCase("system")]
        [TestCase("enlistment")]
        public void FailedReadsNeverSelectTheTrueDefault(string source)
        {
            MockTracer tracer = new MockTracer();
            DevDriveFilterConfig.IsEnabled(
                (string key, out bool? value, out string error) =>
                {
                    value = null;
                    error = "read denied";
                    return false;
                },
                true,
                source,
                tracer,
                out string reason).ShouldBeFalse();
            reason.ShouldContain("read denied");
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase]
        public void MissingGitReaderPreventsAnUpdate()
        {
            DevDriveFilterConfig.IsEnabled(null, true, "system", new MockTracer(), out string reason).ShouldBeFalse();
            reason.ShouldContain("Git is unavailable");
        }

        [TestCase(0, "true\n", "", true, true)]
        [TestCase(0, "false\n", "", true, false)]
        [TestCase(1, "", "", true, null)]
        [TestCase(1, "", "warning: read failed", false, null)]
        [TestCase(2, "", "", false, null)]
        [TestCase(128, "", "warning: read failed", false, null)]
        [TestCase(-1073741502, "", "", false, null)]
        [TestCase(0, "invalid", "", false, null)]
        public void SystemBooleanReaderPinsIncludesAndStrictExitHandling(
            int exit, string output, string errors, bool expectedSuccess, bool? expectedValue)
        {
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.System,
                (arguments, cancellation) =>
                {
                    arguments.ShouldEqual("config --system --includes --type=bool gvfs.auto-allow-devdrive-filter");
                    return new GitProcess.Result(output, errors, exit);
                },
                TimeSpan.FromSeconds(1),
                new MockTracer(),
                out bool? value,
                out string error).ShouldEqual(expectedSuccess);
            value.ShouldEqual(expectedValue);
            if (!expectedSuccess)
            {
                error.ShouldNotBeNull();
            }
        }

        [TestCase(false, "config --type=bool gvfs.auto-allow-devdrive-filter")]
        [TestCase(true, "config --system --includes --type=bool gvfs.auto-allow-devdrive-filter")]
        public void BooleanReaderSelectsTheRequestedScope(bool systemScope, string expectedArguments)
        {
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                systemScope ? GitConfigBoolScope.System : GitConfigBoolScope.Merged,
                (arguments, cancellation) =>
                {
                    arguments.ShouldEqual(expectedArguments);
                    return new GitProcess.Result("false\n", string.Empty, 0);
                },
                TimeSpan.FromSeconds(1),
                new MockTracer(),
                out bool? value,
                out string error).ShouldBeTrue();
            value.ShouldEqual(false);
            error.ShouldBeNull();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void TruncatedBooleanOutputDoesNotEnableTheFeature(bool outputTruncated, bool errorsTruncated)
        {
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.Merged,
                (arguments, cancellation) => new GitProcess.Result("true", string.Empty, 0, outputTruncated, errorsTruncated),
                TimeSpan.FromSeconds(1),
                new MockTracer(),
                out bool? value,
                out string error).ShouldBeFalse();
            value.HasValue.ShouldBeFalse();
            error.ShouldContain("truncated");
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void TimedOutBooleanReadFailsClosedAndLogs()
        {
            MockTracer tracer = new MockTracer();
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.Merged,
                (arguments, cancellation) =>
                {
                    if (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                    {
                        throw new InvalidOperationException("The timeout did not cancel the read.");
                    }

                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("The timeout did not cancel the read.");
                },
                TimeSpan.FromMilliseconds(20),
                tracer,
                out bool? value,
                out string error).ShouldBeFalse();
            value.HasValue.ShouldBeFalse();
            error.ShouldContain("timed out");
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void FailedProcessStartReportsAnErrorAndLogs()
        {
            MockTracer tracer = new MockTracer();
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.Merged,
                (arguments, cancellation) => throw new Win32Exception(2),
                TimeSpan.FromSeconds(1),
                tracer,
                out bool? value,
                out string error).ShouldBeFalse();
            value.HasValue.ShouldBeFalse();
            error.ShouldContain("failed");
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void TypedProcessFailureReportsAnErrorAndLogs()
        {
            MockTracer tracer = new MockTracer();
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.System,
                (arguments, cancellation) => throw new GitConfigProcessException(new Win32Exception(2)),
                TimeSpan.FromSeconds(1),
                tracer,
                out bool? value,
                out string error).ShouldBeFalse();
            value.HasValue.ShouldBeFalse();
            error.ShouldContain("failed");
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase]
        public void UnsetExitWithOutputIsNotTreatedAsAnAbsentFlag()
        {
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.System,
                (arguments, cancellation) => new GitProcess.Result("false", string.Empty, 1),
                TimeSpan.FromSeconds(1),
                new MockTracer(),
                out bool? value,
                out string error).ShouldBeFalse();
            value.HasValue.ShouldBeFalse();
            error.ShouldContain("unexpected output");
        }

        [TestCase]
        public void BooleanReadReportsSharedRetryTelemetry()
        {
            int calls = 0;
            MockTracer tracer = new MockTracer();
            GitProcessConfigReader.TryReadBool(
                "gvfs.auto-allow-devdrive-filter",
                GitConfigBoolScope.System,
                (arguments, cancellation) => GitProcessConfigReader.Read(
                    "gvfs.auto-allow-devdrive-filter",
                    () => ++calls == 1
                        ? new GitProcess.Result(string.Empty, "warning: unable to access 'mock:\\config': Permission denied", 1)
                        : new GitProcess.Result("false\n", string.Empty, 0),
                    tracer,
                    delay => { }),
                TimeSpan.FromSeconds(1),
                tracer,
                out bool? value,
                out string error).ShouldBeTrue();
            calls.ShouldEqual(2);
            value.ShouldEqual(false);
            error.ShouldBeNull();
            tracer.TelemetryWarnings.Count.ShouldEqual(1);
        }

        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\"}", null)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":false}", false)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":true}", true)]
        public void OldAndNewClientWireMessagesKeepTheNullableVeto(string json, bool? expected)
        {
            NamedPipeMessages.EnableAndAttachProjFSRequest request = NamedPipeMessages.EnableAndAttachProjFSRequest.FromMessage(
                new NamedPipeMessages.Message("EnableAndAttachProjFSRequest", json));
            request.AutoAllowDevDriveFilter.ShouldEqual(expected);
            NamedPipeMessages.EnableAndAttachProjFSRequest.FromMessage(request.ToMessage()).AutoAllowDevDriveFilter.ShouldEqual(expected);
            request.ToMessage().Body.ShouldContain("\"AutoAllowDevDriveFilter\"");
        }
    }
}
