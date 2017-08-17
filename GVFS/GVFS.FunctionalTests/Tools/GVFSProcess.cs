using GVFS.Tests.Should;
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
            string output;
            this.TryMount(out output).ShouldEqual(true, "GVFS did not mount: " + output);
        }

        public bool TryMount(out string output)
        {
            string mountCommand = "mount " + this.enlistmentRoot + " --internal_use_only_service_name " + GVFSServiceProcess.TestServiceName;

            this.IsEnlistmentMounted().ShouldEqual(false, "GVFS is already mounted");
            output = this.CallGVFS(mountCommand);
            return this.IsEnlistmentMounted();
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

        public void Repair()
        {
            this.CallGVFS(
                "repair --confirm " + this.enlistmentRoot, 
                failOnError: true);
        }

        public string Diagnose()
        {
            string diagnoseArgs = string.Join(
                " ",
                "diagnose " + this.enlistmentRoot,
                "--internal_use_only_service_name " + GVFSServiceProcess.TestServiceName);
            return this.CallGVFS(diagnoseArgs);
        }

        public string Status()
        {
            return this.CallGVFS("status " + this.enlistmentRoot);
        }

        public string CacheServer(string args)
        {
            return this.CallGVFS("cache-server " + args + " " + this.enlistmentRoot);
        }

        public void Unmount()
        {
            string unmountArgs = string.Join(
                " ",
                "unmount " + this.enlistmentRoot,
                "--internal_use_only_service_name " + GVFSServiceProcess.TestServiceName);
            string result = this.CallGVFS(unmountArgs);
            this.IsEnlistmentMounted().ShouldEqual(false, "GVFS did not unmount: " + result);
        }

        public bool IsEnlistmentMounted()
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
