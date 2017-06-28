using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using System;
using System.IO;

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

        protected override string VerbName
        {
            get { return UnmountVerbName; }
        }

        public override void Execute()
        {
            this.EnlistmentRootPath =
                !string.IsNullOrWhiteSpace(this.EnlistmentRootPath)
                ? this.EnlistmentRootPath
                : Environment.CurrentDirectory;
            string root = null;
            if (Directory.Exists(this.EnlistmentRootPath))
            {
                root = EnlistmentUtils.GetEnlistmentRoot(this.EnlistmentRootPath);
            }

            if (root == null)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid GVFS enlistment",
                    this.EnlistmentRootPath);
            }

            string errorMessage = null;
            if (!this.ShowStatusWhileRunning(
                () => { return this.RequestUnmount(root, out errorMessage); },
                "Unmounting"))
            {
                this.ReportErrorAndExit(errorMessage);
            }
        }

        private bool RequestUnmount(string rootPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            NamedPipeMessages.UnmountRepoRequest request = new NamedPipeMessages.UnmountRepoRequest();
            request.EnlistmentRoot = rootPath;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "Unable to unmount because GVFS.Service is not responding. Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.UnmountRepoRequest.Response.Header)
                    {
                        NamedPipeMessages.UnmountRepoRequest.Response message = NamedPipeMessages.UnmountRepoRequest.Response.FromMessage(response);

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = message.UserText;
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
    }
}
