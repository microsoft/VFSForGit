using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;

namespace GVFS.CommandLine
{
    [Verb(StatusVerb.StatusVerbName, HelpText = "Get the status of the GVFS virtual repo")]
    public class StatusVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string StatusVerbName = "status";

        protected override string VerbName
        {
            get { return StatusVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            bool isExcluded;
            string errorMessage;
            if (AntiVirusExclusions.TryGetIsPathExcluded(enlistment.EnlistmentRoot, out isExcluded, out errorMessage))
            {
                if (!isExcluded)
                {
                    this.Output.WriteLine(
                        "This repo is not excluded from antivirus.",
                        enlistment.EnlistmentRoot);
                }
            }
            else
            {
                this.Output.WriteLine(
                    "Could not check if '{0}' is excluded from anti-virus. Please check to ensure that '{0}' is excluded. Error: {1}",
                    enlistment.EnlistmentRoot, 
                    errorMessage);
            }

            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                }

                try
                {
                    pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                    NamedPipeMessages.GetStatus.Response getStatusResponse =
                        NamedPipeMessages.GetStatus.Response.FromJson(pipeClient.ReadRawResponse());

                    this.Output.WriteLine("Enlistment root: " + getStatusResponse.EnlistmentRoot);
                    this.Output.WriteLine("Repo URL: " + getStatusResponse.RepoUrl);
                    this.Output.WriteLine("Cache Server: " + getStatusResponse.CacheServer);
                    this.Output.WriteLine("Mount status: " + getStatusResponse.MountStatus);
                    this.Output.WriteLine("GVFS Lock: " + getStatusResponse.LockStatus);
                    this.Output.WriteLine("Background operations: " + getStatusResponse.BackgroundOperationCount);
                    this.Output.WriteLine("Disk layout version: " + getStatusResponse.DiskLayoutVersion);
                }
                catch (BrokenPipeException e)
                {
                    this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
                }
            }
        }
    }
}
