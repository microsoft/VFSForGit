using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System.IO;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [SetUpFixture]
    public class LongRunningSetup
    {
        public static bool OutputLogsWhenDone
        {
            get; set;
        }

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
                if (LongRunningSetup.OutputLogsWhenDone)
                {
                    TestResultsHelper.OutputGVFSLogs(LongRunningSetup.Enlistment);
                }

                LongRunningSetup.Enlistment.UnmountAndDeleteAll();
                LongRunningSetup.Enlistment = null;
            }
        }
    }
}
