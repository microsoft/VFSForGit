using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeNestedPathTests
    {
        [TestCase]
        public void PathInsidePrimaryIsBlocked()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\repo\src\subfolder",
                @"C:\repo\src").ShouldBeTrue();
        }

        [TestCase]
        public void PathEqualToPrimaryIsBlocked()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\repo\src",
                @"C:\repo\src").ShouldBeTrue();
        }

        [TestCase]
        public void PathOutsidePrimaryIsAllowed()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\repo\src.worktrees\wt1",
                @"C:\repo\src").ShouldBeFalse();
        }

        [TestCase]
        public void SiblingPathIsAllowed()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\repo\src2",
                @"C:\repo\src").ShouldBeFalse();
        }

        [TestCase]
        public void PathWithDifferentCaseIsBlocked()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\Repo\SRC\subfolder",
                @"C:\repo\src").ShouldBeTrue();
        }

        [TestCase]
        public void DeeplyNestedPathIsBlocked()
        {
            GVFSEnlistment.IsPathInsideDirectory(
                @"C:\repo\src\a\b\c\d",
                @"C:\repo\src").ShouldBeTrue();
        }
    }
}
