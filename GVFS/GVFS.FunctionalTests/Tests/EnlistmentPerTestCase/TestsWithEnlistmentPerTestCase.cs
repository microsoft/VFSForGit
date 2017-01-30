using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerTestCase
    {
        public GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [SetUp]
        public virtual void CreateEnlistment()
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs);
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
