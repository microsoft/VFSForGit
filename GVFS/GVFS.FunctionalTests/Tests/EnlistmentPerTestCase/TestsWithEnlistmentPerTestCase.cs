using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerTestCase
    {
        private readonly bool forcePerRepoObjectCache;
        private readonly bool unattended;

        public TestsWithEnlistmentPerTestCase(bool forcePerRepoObjectCache = false, bool unattended = false)
        {
            this.forcePerRepoObjectCache = forcePerRepoObjectCache;
            this.unattended = unattended;
        }

        public GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [SetUp]
        public virtual void CreateEnlistment()
        {
            if (this.forcePerRepoObjectCache)
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMountWithPerRepoCache(GVFSTestConfig.PathToGVFS, skipPrefetch: false, unattended: this.unattended);
            }
            else
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(GVFSTestConfig.PathToGVFS, unattended: this.unattended);
            }
        }

        [TearDown]
        public virtual void DeleteEnlistment()
        {
            if (this.Enlistment != null)
            {
                this.Enlistment.UnmountAndDeleteAll();
            }
        }
    }
}
