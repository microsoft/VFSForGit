using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class DehydrateTests : TestsWithEnlistmentPerFixture
    {
        private const int GVFSGenericError = 3;

        [TestCase]
        public void DehydrateShouldExitWithoutConfirm()
        {
            this.DehydrateShouldSucceed("To actually execute the dehydrate, run 'gvfs dehydrate --confirm'", confirm: false);
        }

        [TestCase]
        public void DehydrateShouldSucceedInCommonCase()
        {
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true);
        }

        [TestCase]
        public void DehydrateShouldFailOnUnmountedRepoWithStatus()
        {
            this.Enlistment.UnmountGVFS();
            this.DehydrateShouldFail("Failed to run git status because the repo is not mounted", noStatus: false);
            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void DehydrateShouldFailOnWrongDiskLayoutVersion()
        {
            this.Enlistment.UnmountGVFS();

            string currentVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (currentVersionNum - 1).ToString());
            this.DehydrateShouldFail("disk layout version doesn't match current version", noStatus: true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (currentVersionNum + 1).ToString());
            this.DehydrateShouldFail("disk layout version doesn't match current version", noStatus: true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, currentVersionNum.ToString());

            this.Enlistment.MountGVFS();
        }

        private void DehydrateShouldSucceed(string expectedOutput, bool confirm)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm ? new List<string> { "--confirm" } : new List<string> { });
            result.ExitCode.ShouldEqual(0, "mount exit code was " + result.ExitCode);
            result.Output.ShouldContain(expectedOutput);
        }

        private void DehydrateShouldFail(string expectedErrorMessage, bool noStatus)
        {
            List<string> args = new List<string> { "--confirm" };
            if (noStatus)
            {
                args.Add("--no-status");
            }

            ProcessResult result = this.RunDehydrateProcess(args);
            result.ExitCode.ShouldEqual(GVFSGenericError, $"mount exit code was not {GVFSGenericError}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private ProcessResult RunDehydrateProcess(List<string> arguments)
        {
            string pathToGVFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            ProcessStartInfo processInfo = new ProcessStartInfo(pathToGVFS);
            processInfo.Arguments = "dehydrate " + string.Join(" ", arguments);
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = enlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            return ProcessHelper.Run(processInfo);
        }
    }
}
