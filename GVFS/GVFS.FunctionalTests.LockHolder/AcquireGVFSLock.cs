using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Platform.Linux;
using GVFS.Platform.Mac;
using GVFS.Platform.Windows;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.LockHolder
{
    public class AcquireGVFSLockVerb
    {
        private static string fullCommand = "GVFS.FunctionalTests.LockHolder";

        [Option(
            "skip-release-lock",
            Default = false,
            Required = false,
            HelpText = "Skip releasing the GVFS lock when exiting the program.")]
        public bool NoReleaseLock { get; set; }

        public void Execute()
        {
            string errorMessage;
            string enlistmentRoot;
            if (!TryGetGVFSEnlistmentRootImplementation(Environment.CurrentDirectory, out enlistmentRoot, out errorMessage))
            {
                throw new Exception("Unable to get GVFS Enlistment root: " + errorMessage);
            }

            string enlistmentPipename = GetNamedPipeNameImplementation(enlistmentRoot);

            AcquireLock(enlistmentPipename);

            Console.ReadLine();

            if (!this.NoReleaseLock)
            {
                ReleaseLock(enlistmentPipename, enlistmentRoot);
            }
        }

        private static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
            }

            // Not able to use WindowsPlatform here - because of its dependency on WindowsIdentity (and also kernel32.dll).
            enlistmentRoot = null;

            string finalDirectory;
            if (!WindowsFileSystem.TryGetNormalizedPathImplementation(directory, out finalDirectory, out errorMessage))
            {
                return false;
            }

            const string dotGVFSRoot = ".gvfs";
            enlistmentRoot = Paths.GetRoot(finalDirectory, dotGVFSRoot);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {dotGVFSRoot} in {finalDirectory}";
                return false;
            }

            return true;
        }

        private static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
            }

            // Not able to use WindowsPlatform here - because of its dependency on WindowsIdentity (and also kernel32.dll).
            return "GVFS_" + enlistmentRoot.ToUpper().Replace(':', '_');
        }

        private static void AcquireLock(string enlistmentPipename)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistmentPipename))
            {
                if (!pipeClient.Connect())
                {
                    throw new Exception("The repo does not appear to be mounted. Use 'gvfs status' to check.");
                }

                int pid = Process.GetCurrentProcess().Id;

                string result;
                if (!GVFSLock.TryAcquireGVFSLockForProcess(
                    unattended: false,
                    pipeClient: pipeClient,
                    fullCommand: AcquireGVFSLockVerb.fullCommand,
                    pid: pid,
                    isElevated: false,
                    isConsoleOutputRedirectedToFile: false,
                    checkAvailabilityOnly: false,
                    gvfsEnlistmentRoot: null,
                    gitCommandSessionId: string.Empty,
                    result: out result))
                {
                    throw new Exception(result);
                }
            }
        }

        private static void ReleaseLock(string enlistmentPipename, string enlistmentRoot)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistmentPipename))
            {
                if (!pipeClient.Connect())
                {
                    throw new Exception("The repo does not appear to be mounted. Use 'gvfs status' to check.");
                }

                int pid = Process.GetCurrentProcess().Id;

                NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid: pid, isElevated: false, checkAvailabilityOnly: false, parsedCommand: AcquireGVFSLockVerb.fullCommand, gitCommandSessionId: string.Empty);
                NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.ReleaseLock.Request);

                pipeClient.SendRequest(requestMessage);
                NamedPipeMessages.ReleaseLock.Response response = response = new NamedPipeMessages.ReleaseLock.Response(pipeClient.ReadResponse());
            }
        }
    }
}
