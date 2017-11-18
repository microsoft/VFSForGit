using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class DehydrateTests : TestsWithEnlistmentPerFixture
    {
        private const int RGFSGenericError = 3;

        [TestCase]
        public void DehydrateShouldExitWithoutConfirm()
        {
            this.DehydrateShouldSucceed("To actually execute the dehydrate, run 'rgfs dehydrate --confirm'", confirm: false, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldSucceedInCommonCase()
        {
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldFailOnUnmountedRepoWithStatus()
        {
            this.Enlistment.UnmountRGFS();
            this.DehydrateShouldFail("Failed to run git status because the repo is not mounted", noStatus: false);
            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void DehydrateShouldSucceedEvenIfObjectCacheIsDeleted()
        {
            this.Enlistment.UnmountRGFS();
            CmdRunner.DeleteDirectoryWithRetry(this.Enlistment.ObjectRoot);
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: true);
        }

        [TestCase]
        public void DehydrateShouldFailOnWrongDiskLayoutVersion()
        {
            this.Enlistment.UnmountRGFS();

            string currentVersion = RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, (currentVersionNum - 1).ToString());
            this.DehydrateShouldFail("disk layout version doesn't match current version", noStatus: true);

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, (currentVersionNum + 1).ToString());
            this.DehydrateShouldFail("Changes to RGFS disk layout do not allow mounting after downgrade.", noStatus: true);

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, currentVersionNum.ToString());

            this.Enlistment.MountRGFS();
        }

        private void DehydrateShouldSucceed(string expectedOutput, bool confirm, bool noStatus)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm, noStatus);
            result.ExitCode.ShouldEqual(0, $"mount exit code was {result.ExitCode}. Output: {result.Output}");
            result.Output.ShouldContain(expectedOutput);
        }

        private void DehydrateShouldFail(string expectedErrorMessage, bool noStatus)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm: true, noStatus: noStatus);
            result.ExitCode.ShouldEqual(RGFSGenericError, $"mount exit code was not {RGFSGenericError}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private ProcessResult RunDehydrateProcess(bool confirm, bool noStatus)
        {
            string dehydrateFlags = string.Empty;
            if (confirm)
            {
                dehydrateFlags += " --confirm ";
            }

            if (noStatus)
            {
                dehydrateFlags += " --no-status ";
            }

            string pathToRGFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            ProcessStartInfo processInfo = new ProcessStartInfo(pathToRGFS);
            processInfo.Arguments = "dehydrate " + dehydrateFlags + " --internal_use_only_service_name " + RGFSServiceProcess.TestServiceName;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = enlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            return ProcessHelper.Run(processInfo);
        }
    }
}