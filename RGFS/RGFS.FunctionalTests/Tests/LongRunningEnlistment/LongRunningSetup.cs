using RGFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [SetUpFixture]
    public class LongRunningSetup
    {
        public static RGFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public void CreateEnlistmentAndMount()
        {
            string pathToRgfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            LongRunningSetup.Enlistment = RGFSFunctionalTestEnlistment.CloneAndMount(pathToRgfs);
        }
        
        [OneTimeTearDown]
        public void UnmountAndDeleteEnlistment()
        {
            if (LongRunningSetup.Enlistment != null)
            {
                LongRunningSetup.Enlistment.UnmountAndDeleteAll();
                LongRunningSetup.Enlistment = null;
            }
        }
    }
}
