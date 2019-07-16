using GVFS.Tests.Should;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSProcess
    {
        private const int SuccessExitCode = 0;
        private const int ExitCodeShouldNotBeZero = -1;
        private const int DoNotCheckExitCode = -2;

        private readonly string pathToGVFS;
        private readonly string enlistmentRoot;
        private readonly string localCacheRoot;

        public GVFSProcess(GVFSFunctionalTestEnlistment enlistment)
            : this(GVFSTestConfig.PathToGVFS, enlistment.EnlistmentRoot, Path.Combine(enlistment.EnlistmentRoot, GVFSTestConfig.DotGVFSRoot))
        {
        }

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
            this.CallGVFS(args, expectedExitCode: SuccessExitCode);
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

        public string PruneSparseNoFolders()
        {
            return this.SparseCommand(addFolders: true, shouldPrune: true, shouldSucceed: true, folders: new string[0]);
        }

        public string AddSparseFolders(params string[] folders)
        {
            return this.SparseCommand(addFolders: true, shouldPrune: false, shouldSucceed: true, folders: folders);
        }

        public string AddSparseFolders(bool shouldPrune, params string[] folders)
        {
            return this.SparseCommand(addFolders: true, shouldPrune: shouldPrune, shouldSucceed: true, folders: folders);
        }

        public string AddSparseFolders(bool shouldPrune, bool shouldSucceed, params string[] folders)
        {
            return this.SparseCommand(addFolders: true, shouldPrune: shouldPrune, shouldSucceed: shouldSucceed, folders: folders);
        }

        public string RemoveSparseFolders(params string[] folders)
        {
            return this.SparseCommand(addFolders: false, shouldPrune: false, shouldSucceed: true, folders: folders);
        }

        public string RemoveSparseFolders(bool shouldPrune, params string[] folders)
        {
            return this.SparseCommand(addFolders: false, shouldPrune: shouldPrune, shouldSucceed: true, folders: folders);
        }

        public string RemoveSparseFolders(bool shouldPrune, bool shouldSucceed, params string[] folders)
        {
            return this.SparseCommand(addFolders: false, shouldPrune: shouldPrune, shouldSucceed: shouldSucceed, folders: folders);
        }

        public string SparseCommand(bool addFolders, bool shouldPrune, bool shouldSucceed, params string[] folders)
        {
            string action = string.Empty;
            string folderList = string.Empty;
            string pruneArg = shouldPrune ? "--prune" : string.Empty;

            if (folders.Length > 0)
            {
                action = addFolders ? "-a" : "-r";
                folderList = string.Join(";", folders);
                if (folderList.Contains(" "))
                {
                    folderList = $"\"{folderList}\"";
                }
            }

            return this.Sparse($"{pruneArg} {action} {folderList}", shouldSucceed);
        }

        public string Sparse(string arguments, bool shouldSucceed)
        {
            return this.CallGVFS($"sparse {this.enlistmentRoot} {arguments}", expectedExitCode: shouldSucceed ? SuccessExitCode : ExitCodeShouldNotBeZero);
        }

        public string[] GetSparseFolders()
        {
            string output = this.CallGVFS($"sparse {this.enlistmentRoot} -l");
            if (output.StartsWith("No folders in sparse list."))
            {
                return new string[0];
            }

            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string Prefetch(string args, bool failOnError, string standardInput = null)
        {
            return this.CallGVFS("prefetch \"" + this.enlistmentRoot + "\" " + args, failOnError ? SuccessExitCode : DoNotCheckExitCode, standardInput: standardInput);
        }

        public void Repair(bool confirm)
        {
            string confirmArg = confirm ? "--confirm " : string.Empty;
            this.CallGVFS(
                "repair " + confirmArg + "\"" + this.enlistmentRoot + "\"",
                expectedExitCode: SuccessExitCode);
        }

        public string LooseObjectStep()
        {
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                expectedExitCode: SuccessExitCode,
                internalParameter: GVFSHelpers.GetInternalParameter("\\\"LooseObjects\\\""));
        }

        public string PackfileMaintenanceStep(long? batchSize)
        {
            string sizeString = batchSize.HasValue ? $"\\\"{batchSize.Value}\\\"" : "null";
            string internalParameter = GVFSHelpers.GetInternalParameter("\\\"PackfileMaintenance\\\"", sizeString);
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                expectedExitCode: SuccessExitCode,
                internalParameter: internalParameter);
        }

        public string PostFetchStep()
        {
            string internalParameter = GVFSHelpers.GetInternalParameter("\\\"PostFetch\\\"");
            return this.CallGVFS(
                "dehydrate \"" + this.enlistmentRoot + "\"",
                expectedExitCode: SuccessExitCode,
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
                return this.CallGVFS("health \"" + this.enlistmentRoot + '"');
            }
            else
            {
                return this.CallGVFS("health -d \"" + directory + "\" \"" + this.enlistmentRoot + '"');
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
                string result = this.CallGVFS("unmount \"" + this.enlistmentRoot + "\"", expectedExitCode: SuccessExitCode);
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
            return this.CallGVFS("service " + argument, expectedExitCode: SuccessExitCode);
        }

        public string ReadConfig(string key, bool failOnError)
        {
            return this.CallGVFS($"config {key}", failOnError ? SuccessExitCode : DoNotCheckExitCode).TrimEnd('\r', '\n');
        }

        public void WriteConfig(string key, string value)
        {
            this.CallGVFS($"config {key} {value}", expectedExitCode: SuccessExitCode);
        }

        public void DeleteConfig(string key)
        {
            this.CallGVFS($"config --delete {key}", expectedExitCode: SuccessExitCode);
        }

        /// <summary>
        /// Invokes a call to gvfs using the arguments specified
        /// </summary>
        /// <param name="args">The arguments to use when invoking gvfs</param>
        /// <param name="expectedExitCode">
        /// What the expected exit code should be.
        /// >= than 0 to check the exit code explicitly
        /// -1 = Fail if the exit code is 0
        /// -2 = Do not check the exit code (Default)
        /// </param>
        /// <param name="trace">What to set the GIT_TRACE environment variable to</param>
        /// <param name="standardInput">What to write to the standard input stream</param>
        /// <param name="internalParameter">The internal parameter to set in the arguments</param>
        /// <returns></returns>
        private string CallGVFS(string args, int expectedExitCode = DoNotCheckExitCode, string trace = null, string standardInput = null, string internalParameter = null)
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

            // TODO(Linux): remove when GVFS.Service process available; until then, do not output warning which confuses MountTests
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                processInfo.EnvironmentVariables["GVFS_UNATTENDED"] = "1";
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

                if (expectedExitCode >= SuccessExitCode)
                {
                    process.ExitCode.ShouldEqual(expectedExitCode, result);
                }
                else if (expectedExitCode == ExitCodeShouldNotBeZero)
                {
                    process.ExitCode.ShouldNotEqual(SuccessExitCode, "Exit code should not be zero");
                }

                return result;
            }
        }
    }
}
