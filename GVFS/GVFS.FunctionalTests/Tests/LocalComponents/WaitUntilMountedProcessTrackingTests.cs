using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class WaitUntilMountedProcessTrackingTests
    {
        [TestCase]
        public void DetectsLateProcessExitWhilePipeNeverAppears()
        {
            const int FakePid = 24680;
            const int FakeExitCode = -1;
            int snapshotCallCount = 0;
            Func<GVFSEnlistment.MountProcessSnapshot> snapshot = () =>
            {
                int count = Interlocked.Increment(ref snapshotCallCount);
                bool exited = count >= 2;
                return new GVFSEnlistment.MountProcessSnapshot(
                    FakePid,
                    hasExited: exited,
                    exitCode: exited ? FakeExitCode : 0);
            };

            using MockTracer tracer = new MockTracer();
            string errorMessage;
            DateTime start = DateTime.UtcNow;
            bool result = GVFSEnlistment.WaitUntilMounted(
                tracer,
                pipeName: "GVFS_no_such_pipe_for_test_" + Guid.NewGuid().ToString("N"),
                enlistmentRoot: "C:\\fake\\root",
                unattended: false,
                mountProcessStatus: snapshot,
                out errorMessage);
            TimeSpan elapsed = DateTime.UtcNow - start;

            result.ShouldBeFalse();
            errorMessage.ShouldContain(FakePid.ToString());
            errorMessage.ShouldContain(FakeExitCode.ToString());
            Assert.That(elapsed.TotalSeconds, Is.LessThan(10), "WaitUntilMounted must detect late process exit within a few connect retries");
        }
    }
}
