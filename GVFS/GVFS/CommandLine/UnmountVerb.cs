using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.CommandLine
{
    [Verb(UnmountVerb.UnmountVerbName, HelpText = "Unmount a GVFS virtual repo")]
    public class UnmountVerb : GVFSVerb.ForExistingEnlistment
    {
        public const string UnmountVerbName = "unmount";

        protected override string VerbName
        {
            get { return UnmountVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment, ITracer tracer = null)
        {
            try
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                {
                    if (!pipeClient.Connect())
                    {
                        this.ReportErrorAndExit("Unable to connect to GVFS");
                    }

                    pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                    string rawGetStatusResponse = pipeClient.ReadRawResponse();
                    NamedPipeMessages.GetStatus.Response getStatusResponse =
                        NamedPipeMessages.GetStatus.Response.FromJson(rawGetStatusResponse);

                    switch (getStatusResponse.MountStatus)
                    {
                        case NamedPipeMessages.GetStatus.Mounting:
                            this.ReportErrorAndExit("Still mounting, please try again later");
                            break;

                        case NamedPipeMessages.GetStatus.Unmounting:
                            this.ReportErrorAndExit("Already unmounting, please wait");
                            break;

                        case NamedPipeMessages.GetStatus.Ready:
                            this.Output.WriteLine("Repo is mounted.  Starting to unmount...");
                            break;

                        case NamedPipeMessages.GetStatus.MountFailed:
                            this.Output.WriteLine("Previous mount attempt failed, run 'gvfs log' for details.");
                            this.Output.WriteLine("Attempting to unmount anyway...");
                            break;

                        default:
                            this.ReportErrorAndExit("Unrecognized response to GetStatus: {0}", rawGetStatusResponse);
                            break;
                    }

                    pipeClient.SendRequest(NamedPipeMessages.Unmount.Request);
                    string unmountResponse = pipeClient.ReadRawResponse();

                    switch (unmountResponse)
                    {
                        case NamedPipeMessages.Unmount.Acknowledged:
                            this.Output.WriteLine("Unmount was acknowledged.  Waiting for complete unmount...");
                            string finalResponse = pipeClient.ReadRawResponse();
                            if (finalResponse == NamedPipeMessages.Unmount.Completed)
                            {
                                this.Output.WriteLine("Unmount completed");
                            }
                            else
                            {
                                this.ReportErrorAndExit("Unrecognized final response to unmount: " + finalResponse);
                            }

                            break;

                        case NamedPipeMessages.Unmount.NotMounted:
                            this.ReportErrorAndExit("Unable to unmount, repo was not mounted");
                            break;

                        case NamedPipeMessages.Unmount.MountFailed:
                            this.ReportErrorAndExit("Unable to unmount, previous mount attempt failed");
                            break;

                        default:
                            this.ReportErrorAndExit("Unrecognized response to Unmount: " + unmountResponse);
                            break;
                    }
                }
            }
            catch (BrokenPipeException e)
            {
                this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
            }
        }
    }
}
