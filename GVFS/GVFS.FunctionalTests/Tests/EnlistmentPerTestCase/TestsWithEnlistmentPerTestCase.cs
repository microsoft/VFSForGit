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
            this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(GVFSTestConfig.PathToGVFS);
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
