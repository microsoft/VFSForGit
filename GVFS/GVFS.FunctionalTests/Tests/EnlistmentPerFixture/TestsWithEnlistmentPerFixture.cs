using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerFixture
    {
        private readonly bool forcePerRepoObjectCache;
        private readonly bool skipPrefetchDuringClone;

        public TestsWithEnlistmentPerFixture(bool forcePerRepoObjectCache = false, bool skipPrefetchDuringClone = false)
        {
            this.forcePerRepoObjectCache = forcePerRepoObjectCache;
            this.skipPrefetchDuringClone = skipPrefetchDuringClone;
        }

        public GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public virtual void CreateEnlistment()
        {
            if (this.forcePerRepoObjectCache)
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMountWithPerRepoCache(GVFSTestConfig.PathToGVFS, this.skipPrefetchDuringClone);
            }
            else
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(GVFSTestConfig.PathToGVFS);
            }
        }

        [OneTimeTearDown]
        public virtual void DeleteEnlistment()
        {
            if (this.Enlistment != null)
            {
                this.Enlistment.UnmountAndDeleteAll();
            }
        }
    }
}
