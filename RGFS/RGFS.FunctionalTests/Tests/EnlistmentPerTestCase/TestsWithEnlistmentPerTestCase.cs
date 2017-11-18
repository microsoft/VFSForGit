using RGFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerTestCase
    {
        public RGFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [SetUp]
        public virtual void CreateEnlistment()
        {
            string pathToRgfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            this.Enlistment = RGFSFunctionalTestEnlistment.CloneAndMount(pathToRgfs);
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
