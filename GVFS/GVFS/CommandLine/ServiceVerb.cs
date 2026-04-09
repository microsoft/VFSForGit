using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(ServiceVerbName, HelpText = "Runs commands for the GVFS service.")]
    public class ServiceVerb : GVFSVerb.ForNoEnlistment
    {
        private const string ServiceVerbName = "service";

        [Option(
            "mount-all",
            Default = false,
            Required = false,
            HelpText = "Mounts all repos")]
        public bool MountAll { get; set; }

        [Option(
            "unmount-all",
            Default = false,
            Required = false,
            HelpText = "Unmounts all repos")]
        public bool UnmountAll { get; set; }

        [Option(
            "list-mounted",
            Default = false,
            Required = false,
            HelpText = "Prints a list of all mounted repos")]
        public bool List { get; set; }

        protected override string VerbName
        {
            get { return ServiceVerbName; }
        }

        public override void Execute()
        {
            int optionCount = new[] { this.MountAll, this.UnmountAll, this.List }.Count(flag => flag);
            if (optionCount == 0)
            {
                this.ReportErrorAndExit($"Error: You must specify an argument.  Run 'gvfs {ServiceVerbName} --help' for details.");
            }
            else if (optionCount > 1)
            {
                this.ReportErrorAndExit($"Error: You cannot specify multiple arguments.  Run 'gvfs {ServiceVerbName} --help' for details.");
            }

            string errorMessage;
            List<string> repoList;
            if (!this.TryGetRepoList(out repoList, out errorMessage))
            {
                this.ReportErrorAndExit("Error getting repo list: " + errorMessage);
            }

            if (this.List)
            {
                foreach (string repoRoot in repoList)
                {
                    if (this.IsRepoMounted(repoRoot))
                    {
                        this.Output.WriteLine(repoRoot);
                    }
                }
            }
            else if (this.MountAll)
            {
                // Always ask the service to ensure that PrjFlt is enabled.  This will ensure that the GVFS installer properly waits for
                // GVFS.Service to finish enabling PrjFlt's AutoLogger
                string error;
                if (!this.TryEnableAndAttachPrjFltThroughService(string.Empty, out error))
                {
                    this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.FilterError, error: $"Failed to enable PrjFlt: {error}");
                }

                List<string> failedRepoRoots = new List<string>();

                foreach (string repoRoot in repoList)
                {
                    if (!this.IsRepoMounted(repoRoot))
                    {
                        this.Output.WriteLine("\r\nMounting repo at " + repoRoot);
                        ReturnCode result = this.Execute<MountVerb>(repoRoot);

                        if (result != ReturnCode.Success)
                        {
                            failedRepoRoots.Add(repoRoot);
                        }
                    }
                }

                if (failedRepoRoots.Count() > 0)
                {
                    string errorString = $"The following repos failed to mount:{Environment.NewLine}{string.Join("\r\n", failedRepoRoots.ToArray())}";
                    Console.Error.WriteLine(errorString);
                    this.ReportErrorAndExit(Environment.NewLine + errorString);
                }
            }
            else if (this.UnmountAll)
            {
                List<string> failedRepoRoots = new List<string>();

                foreach (string repoRoot in repoList)
                {
                    if (this.IsRepoMounted(repoRoot))
                    {
                        this.Output.WriteLine("\r\nUnmounting repo at " + repoRoot);
                        ReturnCode result = this.Execute<UnmountVerb>(
                            repoRoot,
                            verb =>
                            {
                                verb.SkipUnregister = true;
                                verb.SkipLock = true;
                            });

                        if (result != ReturnCode.Success)
                        {
                            failedRepoRoots.Add(repoRoot);
                        }
                    }
                }

                if (failedRepoRoots.Count() > 0)
                {
                    string errorString = $"The following repos failed to unmount:{Environment.NewLine}{string.Join(Environment.NewLine, failedRepoRoots.ToArray())}";
                    Console.Error.WriteLine(errorString);
                    this.ReportErrorAndExit(Environment.NewLine + errorString);
                }
            }
        }

        private bool TryGetRepoList(out List<string> repoList, out string errorMessage)
        {
            repoList = null;
            errorMessage = string.Empty;

            NamedPipeMessages.GetActiveRepoListRequest request = new NamedPipeMessages.GetActiveRepoListRequest();

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "GVFS.Service is not responding.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.GetActiveRepoListRequest.Response.Header)
                    {
                        NamedPipeMessages.GetActiveRepoListRequest.Response message = NamedPipeMessages.GetActiveRepoListRequest.Response.FromMessage(response);

                        if (!string.IsNullOrEmpty(message.ErrorMessage))
                        {
                            errorMessage = message.ErrorMessage;
                        }
                        else
                        {
                            if (message.State != NamedPipeMessages.CompletionState.Success)
                            {
                                errorMessage = "Unable to retrieve repo list.";
                            }
                            else
                            {
                                repoList = message.RepoList;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("GVFS.Service responded with unexpected message: {0}", response);
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with GVFS.Service: " + e.ToString();
                }

                return false;
            }
        }

        private bool IsRepoMounted(string repoRoot)
        {
            // Hide the output of status
            StringWriter statusOutput = new StringWriter();
            ReturnCode result = this.Execute<StatusVerb>(
                repoRoot,
                verb =>
                {
                    verb.Output = statusOutput;
                });

            if (result == ReturnCode.Success)
            {
                return true;
            }

            return false;
        }
    }
}
