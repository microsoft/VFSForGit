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

        public string Prefetch(string args, bool failOnError, string standardInput = null)
        {
            return this.CallGVFS("prefetch \"" + this.enlistmentRoot + "\" " + args, failOnError, standardInput: standardInput);
        }

        public void Repair(bool confirm)
        {
            string confirmArg = confirm ? "--confirm " : string.Empty;
            this.CallGVFS(
                "repair " + confirmArg + "\"" + this.enlistmentRoot + "\"",
                failOnError: true);
        }

        public string LooseObjectStep()
        {
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                failOnError: true,
                internalParameter: GVFSHelpers.GetInternalParameter("\\\"LooseObjects\\\""));
        }

        public string PackfileMaintenanceStep(long? batchSize)
        {
            string sizeString = batchSize.HasValue ? $"\\\"{batchSize.Value}\\\"" : "null";
            string internalParameter = GVFSHelpers.GetInternalParameter("\\\"PackfileMaintenance\\\"", sizeString);
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                failOnError: true,
                internalParameter: internalParameter);
        }

        public string PostFetchStep()
        {
            string internalParameter = GVFSHelpers.GetInternalParameter("\\\"PostFetch\\\"");
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                failOnError: true,
                internalParameter: internalParameter);
        }

        public string Diagnose()
        {
            return this.CallGVFS("diagnose \"" + this.enlistmentRoot + "\"");
        }

        public string Status(string trace = null)
        {
            return this.CallGVFS("status " + this.enlistmentRoot, trace: trace);
        }

        public string Health(string directory = null)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return this.CallGVFS("health " + this.enlistmentRoot);
            }
            else
            {
                return this.CallGVFS("health -d " + directory + " " + this.enlistmentRoot);
            }
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

        public string ReadConfig(string key, bool failOnError)
        {
            return this.CallGVFS($"config {key}", failOnError).TrimEnd('\r', '\n');
        }

        public void WriteConfig(string key, string value)
        {
            this.CallGVFS($"config {key} {value}", failOnError: true);
        }

        public void DeleteConfig(string key)
        {
            this.CallGVFS($"config --delete {key}", failOnError: true);
        }

        private string CallGVFS(string args, bool failOnError = false, string trace = null, string standardInput = null, string internalParameter = null)
        {
            ProcessStartInfo processInfo = null;
            processInfo = new ProcessStartInfo(this.pathToGVFS);

            if (internalParameter == null)
            {
                internalParameter = GVFSHelpers.GetInternalParameter();
            }

            processInfo.Arguments = args + " " + TestConstants.InternalUseOnlyFlag + " " + internalParameter;

            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            if (standardInput != null)
            {
                processInfo.RedirectStandardInput = true;
            }

            if (trace != null)
            {
                processInfo.EnvironmentVariables["GIT_TRACE"] = trace;
            }

            using (Process process = Process.Start(processInfo))
            {
                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }

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
