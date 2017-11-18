using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace RGFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
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
            RGFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            RGFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment();

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot, enlistment2.EnlistmentRoot };

            RGFSProcess rgfsProcess1 = new RGFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS),
                enlistment1.EnlistmentRoot);

            RGFSProcess rgfsProcess2 = new RGFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS),
                enlistment2.EnlistmentRoot);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", expectedRepoRoots: repoRootList);

            // Check both are unmounted
            rgfsProcess1.IsEnlistmentMounted().ShouldEqual(false);
            rgfsProcess2.IsEnlistmentMounted().ShouldEqual(false);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", EmptyRepoList);
            this.RunServiceCommandAndCheckOutput("--mount-all", expectedRepoRoots: repoRootList);

            // Check both are mounted
            rgfsProcess1.IsEnlistmentMounted().ShouldEqual(true);
            rgfsProcess2.IsEnlistmentMounted().ShouldEqual(true);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);
        }

        [TestCase]
        public void ServiceCommandsWithMountAndUnmount()
        {
            RGFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();

            string[] repoRootList = new string[] { enlistment1.EnlistmentRoot };

            RGFSProcess rgfsProcess1 = new RGFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS),
                enlistment1.EnlistmentRoot);

            this.RunServiceCommandAndCheckOutput("--list-mounted", expectedRepoRoots: repoRootList);

            rgfsProcess1.Unmount();

            this.RunServiceCommandAndCheckOutput("--list-mounted", EmptyRepoList, unexpectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--unmount-all", EmptyRepoList, unexpectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--mount-all", EmptyRepoList, unexpectedRepoRoots: repoRootList);

            // Check that it is still unmounted
            rgfsProcess1.IsEnlistmentMounted().ShouldEqual(false);

            rgfsProcess1.Mount();

            this.RunServiceCommandAndCheckOutput("--unmount-all", expectedRepoRoots: repoRootList);
            this.RunServiceCommandAndCheckOutput("--mount-all", expectedRepoRoots: repoRootList);
        }

        private void RunServiceCommandAndCheckOutput(string argument, string[] expectedRepoRoots, string[] unexpectedRepoRoots = null)
        {
            RGFSProcess rgfsProcess = new RGFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS),
                null);

            string result = rgfsProcess.RunServiceVerb(argument);
            result.ShouldContain(expectedRepoRoots);

            if (unexpectedRepoRoots != null)
            {
                result.ShouldNotContain(false, unexpectedRepoRoots);
            }
        }
    }
}
