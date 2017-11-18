using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class CloneTests : TestsWithEnlistmentPerFixture
    {
        private const int RGFSGenericError = 3;
        
        [TestCase]
        public void CloneInsideMountedEnlistment()
        {
            this.SubfolderCloneShouldFail();
        }

        [TestCase]
        public void CloneInsideUnmountedEnlistment()
        {
            this.Enlistment.UnmountRGFS();
            this.SubfolderCloneShouldFail();
            this.Enlistment.MountRGFS();
        }

        private void SubfolderCloneShouldFail()
        {
            string pathToRGFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);

            ProcessStartInfo processInfo = new ProcessStartInfo(pathToRGFS);
            processInfo.Arguments = "clone " + RGFSTestConfig.RepoToClone + " src\\rgfs\\test1";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(RGFSGenericError);
            result.Output.ShouldContain("You can't clone inside an existing RGFS repo");
        }
    }
}
