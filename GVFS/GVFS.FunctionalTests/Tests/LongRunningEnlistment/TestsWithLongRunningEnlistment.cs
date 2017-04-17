using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class TestsWithLongRunningEnlistment
    {
        public GVFSFunctionalTestEnlistment Enlistment
        {
            get { return LongRunningSetup.Enlistment; }
        }

        [TearDown]
        public void CheckTestResult()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                LongRunningSetup.OutputLogsWhenDone = true;
            }
        }
    }
}
