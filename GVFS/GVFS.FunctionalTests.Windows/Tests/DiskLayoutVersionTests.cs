using GVFS.FunctionalTests.Tests.EnlistmentPerTestCase;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class DiskLayoutVersionTests : TestsWithEnlistmentPerTestCase
    {
        private const int CurrentDiskLayoutMinorVersion = 0;

        [TestCase]
        public void MountSucceedsIfMinorVersionHasAdvancedButNotMajorVersion()
        {
            // Advance the minor version, mount should still work
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(
                this.Enlistment.DotGVFSRoot,
                GVFSHelpers.GetCurrentDiskLayoutMajorVersion().ToString(),
                (CurrentDiskLayoutMinorVersion + 1).ToString());
            this.Enlistment.TryMountGVFS().ShouldBeTrue("Mount should succeed because only the minor version advanced");

            // Advance the major version, mount should fail
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(
                this.Enlistment.DotGVFSRoot,
                (GVFSHelpers.GetCurrentDiskLayoutMajorVersion() + 1).ToString(),
                CurrentDiskLayoutMinorVersion.ToString());
            this.Enlistment.TryMountGVFS().ShouldBeFalse("Mount should fail because the major version has advanced");
        }

        [TestCase]
        public void MountFailsIfBeforeMinimumVersion()
        {
            // Mount should fail if on disk version is below minimum supported version
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(
                this.Enlistment.DotGVFSRoot,
                (GVFSHelpers.GetCurrentDiskLayoutMinimumMajorVersion() - 1).ToString(),
                CurrentDiskLayoutMinorVersion.ToString());
            this.Enlistment.TryMountGVFS().ShouldBeFalse("Mount should fail because we are before minimum version");
        }
    }
}
