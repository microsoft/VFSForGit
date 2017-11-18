using CommandLine;
using RGFS.Common;
using RGFS.Common.Git;
using RGFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RGFS.CommandLine
{
    [Verb(ServiceVerbName, HelpText = "Runs commands for the RGFS service.")]
    public class ServiceVerb : RGFSVerb
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

        public override string EnlistmentRootPath
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }

        protected override string VerbName
        {
            get { return ServiceVerbName; }
        }

        public override void Execute()
        {
            int optionCount = new[] { this.MountAll, this.UnmountAll, this.List }.Count(flag => flag);
            if (optionCount == 0)
            {
                this.ReportErrorAndExit("Error: You must specify an argument.  Run 'rgfs serivce --help' for details.");
            }
            else if (optionCount > 1)
            {
                this.ReportErrorAndExit("Error: You cannot specify multiple arguments.  Run 'rgfs serivce --help' for details.");
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
                    this.Output.WriteLine(repoRoot);
                }
            }
            else if (this.MountAll)
            {
                List<string> failedRepoRoots = new List<string>();

                foreach (string repoRoot in repoList)
                {
                    if (!this.IsValidRepo(repoRoot))
                    {
                        continue;
                    }

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
                    this.ReportErrorAndExit("\r\nThe following repos failed to mount:\r\n" + string.Join("\r\n", failedRepoRoots.ToArray()));
                }
            }
            else if (this.UnmountAll)
            {
                List<string> failedRepoRoots = new List<string>();

                foreach (string repoRoot in repoList)
                {
                    if (!this.IsValidRepo(repoRoot))
                    {
                        continue;
                    }

                    if (this.IsRepoMounted(repoRoot))
                    {
                        this.Output.WriteLine("\r\nUnmounting repo at " + repoRoot);
                        ReturnCode result = this.Execute<UnmountVerb>(
                            repoRoot,
                            verb =>
                            {
                                verb.SkipUnregister = true;
                            });

                        if (result != ReturnCode.Success)
                        {
                            failedRepoRoots.Add(repoRoot);
                        }
                    }
                }

                if (failedRepoRoots.Count() > 0)
                {
                    this.ReportErrorAndExit("\r\nThe following repos failed to unmount:\r\n" + string.Join("\r\n", failedRepoRoots.ToArray()));
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
                    errorMessage = "RGFS.Service is not responding.";
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
                        errorMessage = string.Format("RGFS.Service responded with unexpected message: {0}", response);
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with RGFS.Service: " + e.ToString();
                }

                return false;
            }
        }

        private bool IsValidRepo(string repoRoot)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            string hooksPath = this.GetRGFSHooksPathAndCheckVersion(tracer: null);
            RGFSEnlistment enlistment = null;

            try
            {
                enlistment = RGFSEnlistment.CreateFromDirectory(repoRoot, gitBinPath, hooksPath);
            }
            catch (InvalidRepoException)
            {
                return false;
            }

            if (enlistment == null)
            {
                return false;
            }

            return true;
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
