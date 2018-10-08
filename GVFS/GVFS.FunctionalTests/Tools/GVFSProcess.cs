using GVFS.Tests.Should;
using System.Diagnostics;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSProcess
    {
        private readonly string pathToGVFS;
        private readonly string enlistmentRoot;
        private readonly string localCacheRoot;

        public GVFSProcess(string pathToGVFS, string enlistmentRoot, string localCacheRoot)
        {
            this.pathToGVFS = pathToGVFS;
            this.enlistmentRoot = enlistmentRoot;
            this.localCacheRoot = localCacheRoot;
        }

        public void Clone(string repositorySource, string branchToCheckout, bool skipPrefetch)
        {
            string args = string.Format(
                "clone \"{0}\" \"{1}\" --branch \"{2}\" --local-cache-path \"{3}\" {4}",
                repositorySource,
                this.enlistmentRoot,
                branchToCheckout,
                this.localCacheRoot,
                skipPrefetch ? "--no-prefetch" : string.Empty);
            this.CallGVFS(args, failOnError: true);
        }

        public void Mount()
        {
            string output;
            this.TryMount(out output).ShouldEqual(true, "GVFS did not mount: " + output);
            output.ShouldNotContain(ignoreCase: true, unexpectedSubstrings: "warning");
        }

        public bool TryMount(out string output)
        {
            this.IsEnlistmentMounted().ShouldEqual(false, "GVFS is already mounted");
            output = this.CallGVFS("mount \"" + this.enlistmentRoot + "\"");
            return this.IsEnlistmentMounted();
        }

        public string Prefetch(string args, bool failOnError)
        {
            return this.CallGVFS("prefetch \"" + this.enlistmentRoot + "\" " + args, failOnError);
        }

        public void Repair(bool confirm)
        {
            string confirmArg = confirm ? "--confirm " : string.Empty;
            this.CallGVFS(
                "repair " + confirmArg + "\"" + this.enlistmentRoot + "\"",
                failOnError: true);
        }

        public string Diagnose()
        {
            return this.CallGVFS("diagnose \"" + this.enlistmentRoot + "\"");
        }

        public string Status(string trace = null)
        {
            return this.CallGVFS("status " + this.enlistmentRoot, trace: trace);
        }

        public string CacheServer(string args)
        {
            return this.CallGVFS("cache-server " + args + " \"" + this.enlistmentRoot + "\"");
        }

        public void Unmount()
        {
            if (this.IsEnlistmentMounted())
            {
                string result = this.CallGVFS("unmount \"" + this.enlistmentRoot + "\"", failOnError: true);
                this.IsEnlistmentMounted().ShouldEqual(false, "GVFS did not unmount: " + result);
            }
        }

        public bool IsEnlistmentMounted()
        {
            string statusResult = this.CallGVFS("status \"" + this.enlistmentRoot + "\"");
            return statusResult.Contains("Mount status: Ready");
        }

        public string RunServiceVerb(string argument)
        {
            return this.CallGVFS("service " + argument, failOnError: true);
        }

        private string CallGVFS(string args, bool failOnError = false, string trace = null)
        {
            ProcessStartInfo processInfo = null;
            processInfo = new ProcessStartInfo(this.pathToGVFS);
            processInfo.Arguments = args + " --internal_use_only_service_name " + GVFSServiceProcess.TestServiceName;

            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            if (trace != null)
            {
                processInfo.EnvironmentVariables["GIT_TRACE"] = trace;
            }

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
