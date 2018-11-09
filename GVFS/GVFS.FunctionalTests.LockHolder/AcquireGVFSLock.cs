using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Platform.Windows;
using System;
using System.Diagnostics;

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
            string normalizedCurrentDirectory;
            string errorMessage;
            if (!WindowsFileSystem.TryGetNormalizedPathImplementation(Environment.CurrentDirectory, out normalizedCurrentDirectory, out errorMessage))
            {
                throw new Exception("Unable to get normalized path: " + errorMessage);
            }

            string enlistmentRoot;
            if (!WindowsPlatform.TryGetGVFSEnlistmentRootImplementation(Environment.CurrentDirectory, out enlistmentRoot, out errorMessage))
            {
                throw new Exception("Unable to get GVFS Enlistment root: " + errorMessage);
            }

            string enlistmentPipename = GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot);

            AcquireLock(enlistmentPipename);

            Console.ReadLine();

            if (!this.NoReleaseLock)
            {
                ReleaseLock(enlistmentPipename, enlistmentRoot);
            }
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

                NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid: pid, isElevated: false, checkAvailabilityOnly: false, parsedCommand: AcquireGVFSLockVerb.fullCommand);
                NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.ReleaseLock.Request);

                pipeClient.SendRequest(requestMessage);
                NamedPipeMessages.ReleaseLock.Response response = response = new NamedPipeMessages.ReleaseLock.Response(pipeClient.ReadResponse());
            }
        }
    }
}
