using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [NonParallelizable]
    public class ServiceVerbTests : TestsWithMultiEnlistment
    {
        private static readonly string[] EmptyRepoList = new string[] { };

        [TestCase]
        public void ServiceCommandsWithNoRepos()
        {
            this.RunServiceCommandAndCheckOutput("--unmount-all", EmptyRepoList);
            this.RunServiceCommandAndCheckOutput("--mount-all", EmptyRepoList);
            this.RunServiceCommandAndCheckOutput("--list-mounted", EmptyRepoList);
        }

        [TestCase]
        public void ServiceCommandsWithMultipleRepos()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment();

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot, enlistment2.EnlistmentRoot };

            GVFSProcess gvfsProcess1 = new GVFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS),
                enlistment1.EnlistmentRoot,
                enlistment1.LocalCacheRoot);

            GVFSProcess gvfsProcess2 = new GVFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS),
                enlistment2.EnlistmentRoot,
                enlistment2.LocalCacheRoot);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", expectedRepoRoots: repoRootList);

            // Check both are unmounted
            gvfsProcess1.IsEnlistmentMounted().ShouldEqual(false);
            gvfsProcess2.IsEnlistmentMounted().ShouldEqual(false);

            this.RunServiceCommandAndCheckOutput("--list-mounted", EmptyRepoList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", EmptyRepoList);
            this.RunServiceCommandAndCheckOutput("--mount-all", expectedRepoRoots: repoRootList);

            // Check both are mounted
            gvfsProcess1.IsEnlistmentMounted().ShouldEqual(true);
            gvfsProcess2.IsEnlistmentMounted().ShouldEqual(true);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);
        }

        [TestCase]
        public void ServiceCommandsWithMountAndUnmount()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot };

            GVFSProcess gvfsProcess1 = new GVFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS),
                enlistment1.EnlistmentRoot,
                enlistment1.LocalCacheRoot);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);

            gvfsProcess1.Unmount();

            this.RunServiceCommandAndCheckOutput("--list-mounted", EmptyRepoList, unexpectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", EmptyRepoList, unexpectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--mount-all", EmptyRepoList, unexpectedRepoRoots: repoRootList);

            // Check that it is still unmounted
            gvfsProcess1.IsEnlistmentMounted().ShouldEqual(false);

            gvfsProcess1.Mount();

            this.RunServiceCommandAndCheckOutput("--unmount-all", expectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--mount-all", expectedRepoRoots: repoRootList);
        }

        private void RunServiceCommandAndCheckOutput(string argument, string[] expectedRepoRoots, string[] unexpectedRepoRoots = null)
        {
            GVFSProcess gvfsProcess = new GVFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS), 
                enlistmentRoot: null, 
                localCacheRoot: null);

            string result = gvfsProcess.RunServiceVerb(argument);
            result.ShouldContain(expectedRepoRoots);

            if (unexpectedRepoRoots != null)
            {
                result.ShouldNotContain(false, unexpectedRepoRoots);
            }
        }
    }
}
