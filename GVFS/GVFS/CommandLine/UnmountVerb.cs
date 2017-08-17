using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using System.Diagnostics;

namespace GVFS.CommandLine
{
    [Verb(UnmountVerb.UnmountVerbName, HelpText = "Unmount a GVFS virtual repo")]
    public class UnmountVerb : GVFSVerb
    {
        private const string UnmountVerbName = "unmount";

        [Value(
            0,
            Required = false,
            Default = "",
            MetaName = "Enlistment Root Path",
            HelpText = "Full or relative path to the GVFS enlistment root")]
        public override string EnlistmentRootPath { get; set; }

        [Option(
            GVFSConstants.VerbParameters.Unmount.SkipLock,
            Default = false,
            Required = false,
            HelpText = "Force unmount even if the lock is not available.")]
        public bool SkipLock { get; set; }

        protected override string VerbName
        {
            get { return UnmountVerbName; }
        }

        public override void Execute()
        {
            string root = Paths.GetGVFSEnlistmentRoot(this.EnlistmentRootPath);
            if (root == null)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid GVFS enlistment",
                    this.EnlistmentRootPath);
            }

            if (!this.SkipLock)
            {
                this.AcquireLock(root);
            }

            string errorMessage = null;
            if (!this.ShowStatusWhileRunning(
                () => { return this.Unmount(root, out errorMessage); },
                "Unmounting"))
            {
                this.ReportErrorAndExit(errorMessage);
            }

            if (!this.ShowStatusWhileRunning(
                () => { return this.UnregisterRepo(root, out errorMessage); }, 
                "Unregistering automount"))
            {
                this.Output.WriteLine("    WARNING: " + errorMessage);
            }
        }

        private bool Unmount(string enlistmentRoot, out string errorMessage)
        {
            errorMessage = string.Empty;

            string pipeName = Paths.GetNamedPipeName(enlistmentRoot);
            string rawGetStatusResponse = string.Empty;

            try
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(pipeName))
                {
                    if (!pipeClient.Connect())
                    {
                        errorMessage = "Unable to connect to GVFS.Mount";
                        return false;
                    }

                    pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                    rawGetStatusResponse = pipeClient.ReadRawResponse();
                    NamedPipeMessages.GetStatus.Response getStatusResponse =
                        NamedPipeMessages.GetStatus.Response.FromJson(rawGetStatusResponse);

                    switch (getStatusResponse.MountStatus)
                    {
                        case NamedPipeMessages.GetStatus.Mounting:
                            errorMessage = "Still mounting, please try again later";
                            return false;

                        case NamedPipeMessages.GetStatus.Unmounting:
                            errorMessage = "Already unmounting, please wait";
                            return false;

                        case NamedPipeMessages.GetStatus.Ready:
                            break;

                        case NamedPipeMessages.GetStatus.MountFailed:
                            break;

                        default:
                            errorMessage = "Unrecognized response to GetStatus: " + rawGetStatusResponse;
                            return false;
                    }

                    pipeClient.SendRequest(NamedPipeMessages.Unmount.Request);
                    string unmountResponse = pipeClient.ReadRawResponse();

                    switch (unmountResponse)
                    {
                        case NamedPipeMessages.Unmount.Acknowledged:
                            string finalResponse = pipeClient.ReadRawResponse();
                            if (finalResponse == NamedPipeMessages.Unmount.Completed)
                            {
                                errorMessage = string.Empty;
                                return true;
                            }
                            else
                            {
                                errorMessage = "Unrecognized final response to unmount: " + finalResponse;
                                return false;
                            }

                        case NamedPipeMessages.Unmount.NotMounted:
                            errorMessage = "Unable to unmount, repo was not mounted";
                            return false;

                        case NamedPipeMessages.Unmount.MountFailed:
                            errorMessage = "Unable to unmount, previous mount attempt failed";
                            return false;

                        default:
                            errorMessage = "Unrecognized response to unmount: " + unmountResponse;
                            return false;
                    }
                }
            }
            catch (BrokenPipeException e)
            {
                errorMessage = "Unable to communicate with GVFS: " + e.ToString();
                return false;
            }
        }

        private bool UnregisterRepo(string rootPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            NamedPipeMessages.UnregisterRepoRequest request = new NamedPipeMessages.UnregisterRepoRequest();
            request.EnlistmentRoot = rootPath;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "Unable to unregister repo because GVFS.Service is not responding. Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.UnregisterRepoRequest.Response.Header)
                    {
                        NamedPipeMessages.UnregisterRepoRequest.Response message = NamedPipeMessages.UnregisterRepoRequest.Response.FromMessage(response);

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = message.ErrorMessage;
                            return false;
                        }
                        else
                        {
                            errorMessage = string.Empty;
                            return true;
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("GVFS.Service responded with unexpected message: {0}", response);
                        return false;
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with GVFS.Service: " + e.ToString();
                    return false;
                }
            }
        }

        private void AcquireLock(string enlistmentRoot)
        {
            string pipeName = Paths.GetNamedPipeName(enlistmentRoot);
            using (NamedPipeClient pipeClient = new NamedPipeClient(pipeName))
            {
                try
                {
                    if (!pipeClient.Connect())
                    {
                        this.ReportErrorAndExit("Unable to connect to GVFS while acquiring lock to unmount.  Try 'gvfs status' to verify if the repo is mounted.");
                        return;
                    }

                    Process currentProcess = Process.GetCurrentProcess();
                    string result = null;
                    if (!GVFSLock.TryAcquireGVFSLockForProcess(
                            pipeClient, 
                            "gvfs unmount", 
                            currentProcess.Id, 
                            currentProcess,
                            enlistmentRoot,
                            out result))
                    {
                        this.ReportErrorAndExit("Unable to acquire the lock prior to unmount. " + result);
                    }
                }
                catch (BrokenPipeException)
                {
                    this.ReportErrorAndExit("Unable to acquire the lock prior to unmount.  Try 'gvfs status' to verify if the repo is mounted.");
                }
            }
        }
    }
}
