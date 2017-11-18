using NUnit.Framework;

namespace RGFS.UnitTests.Virtual
{
    [TestFixture]
    public abstract class TestsWithCommonRepo
    {
        protected CommonRepoSetup Repo { get; private set; }

        [SetUp]
        public virtual void TestSetup()
        {
            this.Repo = new CommonRepoSetup();
        }

        [TearDown]
        public virtual void TestTearDown()
        {
            if (this.Repo != null)
            {
                this.Repo.Dispose();
            }
        }
    }
}
