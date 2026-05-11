using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.MultiEnlistmentTests;
using GVFS.FunctionalTests.Tools;
using GVFS.FunctionalTests.Windows.Tests;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Windows.Windows.Tests
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class SharedCacheUpgradeTests : TestsWithMultiEnlistment
    {
        private string localCachePath;
        private string localCacheParentPath;

        private FileSystemRunner fileSystem;

        public SharedCacheUpgradeTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [SetUp]
        public void SetCacheLocation()
        {
            this.localCacheParentPath = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", Guid.NewGuid().ToString("N"));
            this.localCachePath = Path.Combine(this.localCacheParentPath, ".customGVFSCache");
        }

        private GVFSFunctionalTestEnlistment CloneAndMountEnlistment(string branch = null)
        {
            return this.CreateNewEnlistment(this.localCachePath, branch);
        }
    }
}
