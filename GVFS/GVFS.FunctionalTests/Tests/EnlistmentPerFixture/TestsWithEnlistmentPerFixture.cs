using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerFixture
    {
        private readonly bool forcePerRepoObjectCache;
        
        public TestsWithEnlistmentPerFixture(bool forcePerRepoObjectCache = false)
        {
            this.forcePerRepoObjectCache = forcePerRepoObjectCache;
        }

        public GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public virtual void CreateEnlistment()
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            if (this.forcePerRepoObjectCache)
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMountWithPerRepoCache(pathToGvfs);
            }
            else
            {
                this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs);
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
