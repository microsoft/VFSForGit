using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Net;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class GVFSBuildTest : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem = FileSystemRunner.DefaultRunner;
        private CmdRunner cmdRunner = new CmdRunner();

        [TestCase, Order(1)]
        [Category(CategoryConstants.Build)]
        public void RestoreNuGetPackagesForGVFS()
        {
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout master");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "branch --unset-upstream");

            string nugetPath;
            if (File.Exists(Settings.Default.PathToNuget))
            {
                nugetPath = Settings.Default.PathToNuget;
            }
            else
            {
                nugetPath = this.Enlistment.GetVirtualPathTo("nuget.exe");
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile("http://dist.nuget.org/win-x86-commandline/latest/nuget.exe", nugetPath);
                }
            }

            nugetPath.ShouldBeAFile(this.fileSystem);

            string gvfsSolutionPath = this.GetGVFSSolutionPath();
            gvfsSolutionPath.ShouldBeAFile(this.fileSystem);
            string logFile = this.Enlistment.GetVirtualPathTo("nuget.log");

            this.cmdRunner.RunCommand(string.Format("{0} restore {1} > {2} 2>&1", nugetPath, gvfsSolutionPath, logFile));

            this.Enlistment.GetVirtualPathTo(logFile).ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(
                "Restoring NuGet package Microsoft.Diagnostics.Tracing.EventSource", 
                "Adding package 'Microsoft.Diagnostics.Tracing.EventSource");
        }

        [TestCase, Order(2)]
        [Category(CategoryConstants.Build)]
        public void BuildGVFSWithMSBuild()
        {
            const string CleanBuildFormatString = "\"\"{0}\" & msbuild.exe /maxcpucount /nodeReuse:False /t:Clean /verbosity:quiet /fl1 /flp1:LogFile=\"{1}\" \"{2}\"";

            // Clean
            string preBuildCleanLogPath = this.Enlistment.GetVirtualPathTo("msbuild_preBuildClean.log");
            string preBuildCleanCommand = string.Format(
                CleanBuildFormatString,
                Settings.Default.PathToVSDevBat, 
                preBuildCleanLogPath, 
                this.GetGVFSSolutionPath());
            this.cmdRunner.RunCommand(preBuildCleanCommand);

            preBuildCleanLogPath.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(
                "Build succeeded.", 
                "0 Error(s)");

            // Build
            string buildLogPath = this.Enlistment.GetVirtualPathTo("msbuild_build.log");
            string buildCommand = string.Format(
                "\"\"{0}\" & msbuild.exe /maxcpucount /nodeReuse:False /verbosity:quiet /fl1 /flp1:LogFile=\"{1}\" \"{2}\"",
                Settings.Default.PathToVSDevBat,
                buildLogPath,
                this.GetGVFSSolutionPath());
            this.cmdRunner.RunCommand(buildCommand);

            buildLogPath.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(
                "Build succeeded.", 
                "0 Error(s)");

            // Clean
            string postBuildCleanLogPath = this.Enlistment.GetVirtualPathTo("msbuild_postBuildClean.log");
            string postBuildCleanCommand = string.Format(
                CleanBuildFormatString,
                Settings.Default.PathToVSDevBat,
                postBuildCleanLogPath,
                this.GetGVFSSolutionPath());
            this.cmdRunner.RunCommand(postBuildCleanCommand);

            postBuildCleanLogPath.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(
                "Build succeeded.", 
                "0 Error(s)");
        }

        private string GetGVFSSolutionPath()
        {
            return this.Enlistment.GetVirtualPathTo("GVFS.sln");
        }
    }
}
