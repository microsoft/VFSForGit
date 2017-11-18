using RGFS.Tests.Should;
using System.Diagnostics;

namespace RGFS.FunctionalTests.Tools
{
    public class RGFSProcess
    {
        private readonly string pathToRGFS;
        private readonly string enlistmentRoot;
        
        public RGFSProcess(string pathToRGFS, string enlistmentRoot)
        {
            this.pathToRGFS = pathToRGFS;
            this.enlistmentRoot = enlistmentRoot;
        }
        
        public void Clone(string repositorySource, string branchToCheckout)
        {
            string args = string.Format(
                "clone \"{0}\" \"{1}\" --branch \"{2}\" --no-mount --no-prefetch",
                repositorySource,
                this.enlistmentRoot,
                branchToCheckout);
            this.CallRGFS(args, failOnError: true);
        }

        public void Mount()
        {
            string output;
            this.TryMount(out output).ShouldEqual(true, "RGFS did not mount: " + output);
        }

        public bool TryMount(out string output)
        {
            string mountCommand = "mount " + this.enlistmentRoot + " --internal_use_only_service_name " + RGFSServiceProcess.TestServiceName;

            this.IsEnlistmentMounted().ShouldEqual(false, "RGFS is already mounted");
            output = this.CallRGFS(mountCommand);
            return this.IsEnlistmentMounted();
        }

        public string Prefetch(string args)
        {
            return this.CallRGFS("prefetch " + this.enlistmentRoot + " " + args);
        }

        public void Repair()
        {
            this.CallRGFS(
                "repair --confirm " + this.enlistmentRoot, 
                failOnError: true);
        }

        public string Diagnose()
        {
            string diagnoseArgs = string.Join(
                " ",
                "diagnose " + this.enlistmentRoot,
                "--internal_use_only_service_name " + RGFSServiceProcess.TestServiceName);
            return this.CallRGFS(diagnoseArgs);
        }

        public string Status()
        {
            return this.CallRGFS("status " + this.enlistmentRoot);
        }

        public string CacheServer(string args)
        {
            return this.CallRGFS("cache-server " + args + " " + this.enlistmentRoot);
        }

        public void Unmount()
        {
            string unmountArgs = string.Join(
                " ",
                "unmount " + this.enlistmentRoot,
                "--internal_use_only_service_name " + RGFSServiceProcess.TestServiceName);
            string result = this.CallRGFS(unmountArgs);
            this.IsEnlistmentMounted().ShouldEqual(false, "RGFS did not unmount: " + result);
        }

        public bool IsEnlistmentMounted()
        {
            string statusResult = this.CallRGFS("status " + this.enlistmentRoot);
            return statusResult.Contains("Mount status: Ready");
        }

        public string RunServiceVerb(string argument)
        {
            string serviceVerbArgs = string.Join(
                " ",
                "service " + argument,
                "--internal_use_only_service_name " + RGFSServiceProcess.TestServiceName);
            return this.CallRGFS(serviceVerbArgs, failOnError: true);
        }

        private string CallRGFS(string args, bool failOnError = false)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(this.pathToRGFS);
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
