using GVFS.Common;
using GVFS.Common.NamedPipes;
using System;

namespace GVFS.CommandLine
{
    public class StatusVerb : GVFSVerb.ForExistingEnlistment
    {
        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("status", "Get the status of the GVFS virtual repo");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<StatusVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true);

            return cmd;
        }

        private const string StatusVerbName = "status";

        protected override string VerbName
        {
            get { return StatusVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
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
                    this.Output.WriteLine("Local Cache: " + getStatusResponse.LocalCacheRoot);
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
