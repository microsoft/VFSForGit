using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class TestsWithLongRunningEnlistment
    {
        public GVFSFunctionalTestEnlistment Enlistment
        {
            get { return LongRunningSetup.Enlistment; }
        }
    }
}
