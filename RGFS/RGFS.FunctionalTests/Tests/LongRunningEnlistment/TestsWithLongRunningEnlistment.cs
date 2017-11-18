using RGFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace RGFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixture]
    public class TestsWithLongRunningEnlistment
    {
        public RGFSFunctionalTestEnlistment Enlistment
        {
            get { return LongRunningSetup.Enlistment; }
        }
    }
}
