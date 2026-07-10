using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [NonParallelizable]
    public class ServiceVerbTests : TestsWithMultiEnlistment
    {
        private static readonly string[] EmptyRepoList = new string[] { };
        private readonly string fixtureServiceName = "Test.GVFS.Service.ServiceVerbTests." + Guid.NewGuid().ToString("N");

        [OneTimeSetUp]
        public void StartFixtureService()
        {
            GVFSServiceProcess.InstallService(this.fixtureServiceName);
        }

        [OneTimeTearDown]
        public void StopFixtureService()
        {
            GVFSServiceProcess.UninstallService(this.fixtureServiceName);
        }

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
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment(serviceName: this.fixtureServiceName);
            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment(serviceName: this.fixtureServiceName);

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot, enlistment2.EnlistmentRoot };

            GVFSProcess gvfsProcess1 = new GVFSProcess(
                GVFSTestConfig.PathToGVFS,
                enlistment1.EnlistmentRoot,
                enlistment1.LocalCacheRoot,
                this.fixtureServiceName);

            GVFSProcess gvfsProcess2 = new GVFSProcess(
                GVFSTestConfig.PathToGVFS,
                enlistment2.EnlistmentRoot,
                enlistment2.LocalCacheRoot,
                this.fixtureServiceName);

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
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment(serviceName: this.fixtureServiceName);

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot };

            GVFSProcess gvfsProcess1 = new GVFSProcess(
                GVFSTestConfig.PathToGVFS,
                enlistment1.EnlistmentRoot,
                enlistment1.LocalCacheRoot,
                this.fixtureServiceName);

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
                GVFSTestConfig.PathToGVFS,
                enlistmentRoot: null,
                localCacheRoot: null,
                serviceName: this.fixtureServiceName);

            string result = gvfsProcess.RunServiceVerb(argument);
            result.ShouldContain(expectedRepoRoots);

            if (unexpectedRepoRoots != null)
            {
                result.ShouldNotContain(false, unexpectedRepoRoots);
            }
        }
    }
}
