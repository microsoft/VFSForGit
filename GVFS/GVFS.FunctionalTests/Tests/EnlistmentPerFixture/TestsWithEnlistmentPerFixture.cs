using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public abstract class TestsWithEnlistmentPerFixture
    {
        private bool anyTestsFailed = false;

        public TestsWithEnlistmentPerFixture()
        {
        }

        public GVFSFunctionalTestEnlistment Enlistment
        {
            get; private set;
        }

        [OneTimeSetUp]
        public virtual void CreateEnlistment()
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            this.Enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs);
        }

        [TearDown]
        public virtual void CheckTestResult()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                this.anyTestsFailed = true;
            }
        }

        [OneTimeTearDown]
        public virtual void DeleteEnlistment()
        {
            if (this.Enlistment != null)
            {
                if (this.anyTestsFailed)
                {
                    TestResultsHelper.OutputGVFSLogs(this.Enlistment);
                }

                this.Enlistment.UnmountAndDeleteAll();
            }
        }
    }
}
