using RGFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerFixture
    {
        public TestsWithEnlistmentPerFixture()
        {
        }

        public RGFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public virtual void CreateEnlistment()
        {
            string pathToRgfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            this.Enlistment = RGFSFunctionalTestEnlistment.CloneAndMount(pathToRgfs);
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
