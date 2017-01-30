using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.CommandLine
{
    [Verb(StatusVerb.StatusVerbName, HelpText = "Get the status of the GVFS virtual repo")]
    public class StatusVerb : GVFSVerb.ForExistingEnlistment
    {
        public const string StatusVerbName = "status";

        protected override string VerbName
        {
            get { return StatusVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment, ITracer tracer = null)
        {
            this.CheckAntiVirusExclusion(enlistment);

            this.Output.WriteLine("Attempting to connect to GVFS at {0}...", enlistment.EnlistmentRoot);
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                }

                this.Output.WriteLine("Connected");
                this.Output.WriteLine();

                try
                {
                    pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                    NamedPipeMessages.GetStatus.Response getStatusResponse =
                        NamedPipeMessages.GetStatus.Response.FromJson(pipeClient.ReadRawResponse());

                    this.Output.WriteLine("Mount status: " + getStatusResponse.MountStatus);
                    this.Output.WriteLine("GVFS Lock: " + getStatusResponse.LockStatus);
                    this.Output.WriteLine("Enlistment root: " + getStatusResponse.EnlistmentRoot);
                    this.Output.WriteLine("Repo URL: " + getStatusResponse.RepoUrl);
                    this.Output.WriteLine("Objects URL: " + getStatusResponse.ObjectsUrl);
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
