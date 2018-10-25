using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitProcessTests
    {
        [TestCase]
        public void TryKillRunningProcess_NeverRan()
        {
            GitProcess process = new GitProcess(new MockGVFSEnlistment());
            process.TryKillRunningProcess().ShouldBeTrue();
        }
    }
}
