using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class CloneTests : TestsWithEnlistmentPerFixture
    {
        private const int GVFSGenericError = 3;

        [TestCase]
        public void CloneInsideMountedEnlistment()
        {
            this.SubfolderCloneShouldFail();
        }

        [TestCase]
        public void CloneInsideUnmountedEnlistment()
        {
            this.Enlistment.UnmountGVFS();
            this.SubfolderCloneShouldFail();
            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void CloneWithLocalCachePathWithinSrc()
        {
            string newEnlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();

            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = $"clone {Properties.Settings.Default.RepoToClone} {newEnlistmentRoot} --local-cache-path {Path.Combine(newEnlistmentRoot, "src", ".gvfsCache")}";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = Path.GetDirectoryName(this.Enlistment.EnlistmentRoot);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(GVFSGenericError);
            result.Output.ShouldContain("'--local-cache-path' cannot be inside the src folder");
        }

        [TestCase]
        [Category(Categories.MacOnly)]
        public void CloneWithDefaultLocalCacheLocation()
        {
            FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
            string homeDirectory = Environment.GetEnvironmentVariable("HOME");
            homeDirectory.ShouldBeADirectory(fileSystem);

            string newEnlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();

            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = $"clone {Properties.Settings.Default.RepoToClone} {newEnlistmentRoot} --no-mount --no-prefetch";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(0, result.Errors);

            string dotGVFSRoot = Path.Combine(newEnlistmentRoot, GVFSTestConfig.DotGVFSRoot);
            dotGVFSRoot.ShouldBeADirectory(fileSystem);
            string localCacheRoot = GVFSHelpers.GetPersistedLocalCacheRoot(dotGVFSRoot);
            string gitObjectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(dotGVFSRoot);

            string defaultGVFSCacheRoot = Path.Combine(homeDirectory, ".gvfsCache");
            localCacheRoot.StartsWith(defaultGVFSCacheRoot, StringComparison.Ordinal).ShouldBeTrue($"Local cache root did not default to using {homeDirectory}");
            gitObjectsRoot.StartsWith(defaultGVFSCacheRoot, StringComparison.Ordinal).ShouldBeTrue($"Git objects root did not default to using {homeDirectory}");

            RepositoryHelpers.DeleteTestDirectory(newEnlistmentRoot);
        }

        [TestCase]
        public void CloneToPathWithSpaces()
        {
            GVFSFunctionalTestEnlistment enlistment = GVFSFunctionalTestEnlistment.CloneAndMountEnlistmentWithSpacesInPath(GVFSTestConfig.PathToGVFS);
            enlistment.UnmountAndDeleteAll();
        }

        private void SubfolderCloneShouldFail()
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = "clone " + GVFSTestConfig.RepoToClone + " src\\gvfs\\test1";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(GVFSGenericError);
            result.Output.ShouldContain("You can't clone inside an existing GVFS repo");
        }
    }
}
