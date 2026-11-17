using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WaitUntilMountedProcessTrackingTests
    {
        [TestCase]
        public void ReturnsImmediatelyWhenMountProcessSnapshotReportsExited()
        {
            const int FakePid = 13579;
            const int FakeExitCode = 42;
            int snapshotCallCount = 0;
            Func<GVFSEnlistment.MountProcessSnapshot> snapshot = () =>
            {
                Interlocked.Increment(ref snapshotCallCount);
                return new GVFSEnlistment.MountProcessSnapshot(FakePid, hasExited: true, exitCode: FakeExitCode);
            };

            string errorMessage;
            DateTime start = DateTime.UtcNow;
            bool result = GVFSEnlistment.WaitUntilMounted(
                new MockTracer(),
                pipeName: "GVFS_no_such_pipe_for_test_" + Guid.NewGuid().ToString("N"),
                enlistmentRoot: "C:\\fake\\root",
                unattended: false,
                mountProcessStatus: snapshot,
                out errorMessage);
            TimeSpan elapsed = DateTime.UtcNow - start;

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
            errorMessage.ShouldContain(FakePid.ToString());
            errorMessage.ShouldContain(FakeExitCode.ToString());
            snapshotCallCount.ShouldBeAtLeast(1);

            // The legacy code path would have blocked for the full 60 second
            // pipe timeout. With process tracking we should bail out in well
            // under a second since the snapshot is checked before any
            // connect attempt.
            Assert.That(elapsed.TotalSeconds, Is.LessThan(5), "WaitUntilMounted should bail out quickly when the snapshot reports the mount process exited");
        }

    }
}
