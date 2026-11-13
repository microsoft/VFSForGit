using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

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
            GitProcess.TryGetBoolFromSystemConfig(
                "gvfs.auto-allow-devdrive-filter",
                arguments =>
                {
                    arguments.ShouldEqual("config --system --includes --type=bool gvfs.auto-allow-devdrive-filter");
                    return new GitProcess.Result(output, errors, exit);
                },
                out bool? value,
                out string error).ShouldEqual(expectedSuccess);
            value.ShouldEqual(expectedValue);
            if (!expectedSuccess)
            {
                error.ShouldNotBeNull();
            }
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
