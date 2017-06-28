using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [SetUpFixture]
    public class LongRunningSetup
    {
        public static GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public void CreateEnlistmentAndMount()
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            LongRunningSetup.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs);
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
