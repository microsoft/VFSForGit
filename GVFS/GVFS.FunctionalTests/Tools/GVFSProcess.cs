using GVFS.Tests.Should;
using System;
using System.Diagnostics;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSProcess
    {
        private readonly string pathToGVFS;
        private readonly string enlistmentRoot;

        public GVFSProcess(string pathToGVFS, string enlistmentRoot)
        {
            this.pathToGVFS = pathToGVFS;
            this.enlistmentRoot = enlistmentRoot;
        }

        public void Clone(string repositorySource, string branchToCheckout)
        {
            string args = string.Format(
                "clone \"{0}\" \"{1}\" --branch \"{2}\" --no-mount --no-prefetch",
                repositorySource,
                this.enlistmentRoot,
                branchToCheckout);
            this.CallGVFS(args, failOnError: true);
        }

        public void Mount()
        {
            string mountCommand = "mount " + this.enlistmentRoot;

            this.IsGVFSMounted().ShouldEqual(false, "GVFS is already mounted");
            this.CallGVFS(mountCommand);
            this.IsGVFSMounted().ShouldEqual(true, "GVFS did not mount");
        }

        public string Prefetch(string folderPath)
        {
            string args = "prefetch --verbose --folders \"" + folderPath + "\" " + this.enlistmentRoot;
            return this.CallGVFS(args);
        }

        public string PrefetchFolderBasedOnFile(string filterFilePath)
        {
            string args = "prefetch --verbose --folders-list \"" + filterFilePath + "\" " + this.enlistmentRoot;
            return this.CallGVFS(args);
        }

        public string Diagnose()
        {
            return this.CallGVFS("diagnose " + this.enlistmentRoot);
        }

        public string Status()
        {
            return this.CallGVFS("status " + this.enlistmentRoot);
        }

        public void Unmount()
        {
            this.CallGVFS("unmount " + this.enlistmentRoot);
        }

        private bool IsGVFSMounted()
        {
            string statusResult = this.CallGVFS("status " + this.enlistmentRoot);
            return statusResult.Contains("Mount status: Ready");
        }

        private string CallGVFS(string args, bool failOnError = false)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(this.pathToGVFS);
            processInfo.Arguments = args;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(processInfo))
            {
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (failOnError)
                {
                    process.ExitCode.ShouldEqual(0, result);
                }

                return result;
            }
        }
    }
}
