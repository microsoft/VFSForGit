using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GVFSEnlistmentTests
    {
        private const string MountId = "85576f54f9ab4388bcdc19b4f6c17696";
        private const string EnlistmentId = "520dcf634ce34065a06abaa4010a256f";

        [TestCase]
        public void CanGetMountId()
        {
            TestGVFSEnlistment enlistment = new TestGVFSEnlistment();
            enlistment.GetMountId().ShouldEqual(MountId);
        }

        [TestCase]
        public void CanGetEnlistmentId()
        {
            TestGVFSEnlistment enlistment = new TestGVFSEnlistment();
            enlistment.GetEnlistmentId().ShouldEqual(EnlistmentId);
        }

        private class TestGVFSEnlistment : GVFSEnlistment
        {
            private MockGitProcess gitProcess;

            public TestGVFSEnlistment()
                : base("mock:\\path", "mock://repoUrl", "mock:\\git", authentication: null)
            {
                this.gitProcess = new MockGitProcess();
                this.gitProcess.SetExpectedCommandResult(
                    "config --local gvfs.mount-id",
                    () => new GitProcess.Result(MountId, string.Empty, GitProcess.Result.SuccessCode));
                this.gitProcess.SetExpectedCommandResult(
                    "config --local gvfs.enlistment-id",
                    () => new GitProcess.Result(EnlistmentId, string.Empty, GitProcess.Result.SuccessCode));
            }

            public override GitProcess CreateGitProcess()
            {
                return this.gitProcess;
            }
        }
    }
}
