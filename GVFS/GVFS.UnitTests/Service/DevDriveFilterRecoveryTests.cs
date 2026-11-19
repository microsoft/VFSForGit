using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Service
{
    [TestFixture]
    public class DevDriveFilterRecoveryTests
    {
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\"}", null, false)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":null}", null, false)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":true}", null, true)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\"}", true, true)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":false}", true, false)]
        [TestCase("{\"EnlistmentRoot\":\"mock:\\\\src\",\"AutoAllowDevDriveFilter\":true}", false, false)]
        public void WireAndSystemGateControlWhetherPolicyUpdateRuns(string json, bool? system, bool expectedWrite)
        {
            NamedPipeMessages.EnableAndAttachProjFSRequest request = NamedPipeMessages.EnableAndAttachProjFSRequest.FromMessage(
                new NamedPipeMessages.Message("EnableAndAttachProjFSRequest", json));
            int policyCalls = 0;
            bool result = DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                new MockTracer(),
                () =>
                {
                    bool enabled = DevDriveFilterConfig.IsEnabled(
                        (string key, out bool? value, out string readError) =>
                        {
                            value = system;
                            readError = null;
                            return true;
                        },
                        request.AutoAllowDevDriveFilter,
                        "system",
                        new MockTracer(),
                        out string reason);
                    return (enabled, reason);
                },
                () =>
                {
                    ++policyCalls;
                    return (DevDriveFilterPolicy.AllowFilterResult.Added, null);
                },
                () => (true, false, null),
                out string error);

            result.ShouldEqual(expectedWrite);
            policyCalls.ShouldEqual(expectedWrite ? 1 : 0);
            if (!expectedWrite)
            {
                error.ShouldContain("The mounted volume rejected PrjFlt");
            }
        }

        [TestCase]
        public void DisabledGateDoesNotQueryOrWritePolicy()
        {
            DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                new MockTracer(),
                () => (false, "operator veto"),
                () => throw new InvalidOperationException("Policy must not run."),
                () => throw new InvalidOperationException("Attach must not run."),
                out string error).ShouldBeFalse();
            error.ShouldContain("operator veto");
            error.ShouldContain(
                "fsutil devdrv query mock:\\volume\\",
                "setFiltersAllowed /volume mock:\\volume\\",
                "replaces the volume list",
                "keep its existing entries");
            error.ShouldNotContain(false, "setFiltersAllowed /f");
            error.ShouldNotContain(false, "not on that list");
        }

        [TestCase(DevDriveFilterPolicy.AllowFilterResult.Added)]
        [TestCase(DevDriveFilterPolicy.AllowFilterResult.AlreadyAllowed)]
        [TestCase(DevDriveFilterPolicy.AllowFilterResult.RemountRequired)]
        public void AllowedPolicyRetriesExactlyOnceAndRetainsRemountGuidance(DevDriveFilterPolicy.AllowFilterResult result)
        {
            int calls = 0;
            DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                new MockTracer(),
                () => (true, null),
                () => (result, null),
                () =>
                {
                    ++calls;
                    return (false, true, "rejected");
                },
                out string error).ShouldBeFalse();
            calls.ShouldEqual(1);
            error.ShouldContain("Remount the volume or restart the computer");
            error.ShouldContain("did not force-dismount");
        }

        [TestCase]
        public void OtherAttachErrorsKeepTheCauseAndTheMutationSignal()
        {
            MockTracer tracer = new MockTracer();
            DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                tracer,
                () => (true, null),
                () => (DevDriveFilterPolicy.AllowFilterResult.Added, null),
                () => (false, false, "specific attach error"),
                out string error).ShouldBeFalse();
            error.ShouldEqual("specific attach error");
            tracer.RelatedEventNames.ShouldMatchInOrder("DevDriveFilterRecovery_Summary");
            tracer.RelatedEventMetadata[0]["PolicyResult"].ShouldEqual("Added");
            tracer.RelatedEventMetadata[0]["Outcome"].ShouldEqual("AttachFailed");
        }

        [TestCase(DevDriveFilterPolicy.AllowFilterResult.PolicyUnreadable)]
        [TestCase(DevDriveFilterPolicy.AllowFilterResult.Failed)]
        public void PolicyFailuresDoNotRetryAttachmentOrInventAnAbsentFilter(DevDriveFilterPolicy.AllowFilterResult result)
        {
            DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                new MockTracer(),
                () => (true, null),
                () => (result, "query or write error"),
                () => throw new InvalidOperationException("Attach must not run."),
                out string error).ShouldBeFalse();
            error.ShouldContain("query or write error");
            error.ShouldContain("fsutil devdrv query");
            error.ShouldContain("setFiltersAllowed /volume mock:\\volume\\", "keep its existing entries");
            error.ShouldNotContain(false, "setFiltersAllowed /f");
            error.ShouldNotContain(false, "not on that list");
        }

        [TestCase]
        public void SuccessfulRetryReturnsSuccessWithoutAnError()
        {
            DevDriveFilterRecovery.TryRecover(
                @"mock:\volume\",
                new MockTracer(),
                () => (true, null),
                () => (DevDriveFilterPolicy.AllowFilterResult.Added, null),
                () => (true, false, null),
                out string error).ShouldBeTrue();
            error.ShouldBeNull();
        }
    }
}
