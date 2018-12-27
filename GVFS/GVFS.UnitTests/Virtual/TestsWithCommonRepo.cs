using GVFS.Common;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Virtual
{
    [TestFixture]
    public abstract class TestsWithCommonRepo
    {
        protected CommonRepoSetup Repo { get; private set; }

        [SetUp]
        public virtual void TestSetup()
        {
            this.Repo = new CommonRepoSetup();

            string error;
            RepoMetadata.TryInitialize(
                new MockTracer(),
                this.Repo.Context.FileSystem,
                this.Repo.Context.Enlistment.DotGVFSRoot,
                out error);
        }

        [TearDown]
        public virtual void TestTearDown()
        {
            if (this.Repo != null)
            {
                this.Repo.Dispose();
            }

            RepoMetadata.Shutdown();
        }
    }
}
