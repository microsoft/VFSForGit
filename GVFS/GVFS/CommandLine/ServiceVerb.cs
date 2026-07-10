using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine
{
    public class ServiceVerb : GVFSVerb.ForNoEnlistment
    {
        private const string ServiceVerbName = "service";

        public bool MountAll { get; set; }

        public bool UnmountAll { get; set; }

        public bool List { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("service", "Runs commands for the GVFS service.");

            System.CommandLine.Option<bool> mountAllOption = new System.CommandLine.Option<bool>("--mount-all") { Description = "Mounts all repos" };
            cmd.Add(mountAllOption);

            System.CommandLine.Option<bool> unmountAllOption = new System.CommandLine.Option<bool>("--unmount-all") { Description = "Unmounts all repos" };
            cmd.Add(unmountAllOption);

            System.CommandLine.Option<bool> listMountedOption = new System.CommandLine.Option<bool>("--list-mounted") { Description = "Prints a list of all mounted repos" };
            cmd.Add(listMountedOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForNoEnlistment<ServiceVerb>(cmd, internalOption,
                (verb, result) =>
                {
                    verb.MountAll = result.GetValue(mountAllOption);
                    verb.UnmountAll = result.GetValue(unmountAllOption);
                    verb.List = result.GetValue(listMountedOption);
                });

            return cmd;
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
                    if (this.IsRepoReady(repoRoot))
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
                    if (this.IsRepoAvailableToMount(repoRoot))
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
                    if (this.IsRepoAvailableToUnmount(repoRoot))
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

        private bool IsRepoReady(string repoRoot)
        {
            // For --list-mounted: only a repo whose mount is fully Ready is reported as mounted.
            return this.TryGetRepoMountStatus(repoRoot, out string mountStatus)
                && mountStatus.Equals(NamedPipeMessages.GetStatus.Ready, StringComparison.Ordinal);
        }

        private bool IsRepoAvailableToMount(string repoRoot)
        {
            // For --mount-all: a repo can be mounted only when no live mount process is
            // answering for it (i.e. it is not already Mounting, Ready, Unmounting, or MountFailed).
            return !this.TryGetRepoMountStatus(repoRoot, out _);
        }

        private bool IsRepoAvailableToUnmount(string repoRoot)
        {
            // For --unmount-all: only unmount a repo whose mount is in a state that accepts an
            // unmount request. A repo already Unmounting is reaching the desired state on its own,
            // and one still Mounting rejects the request, so both are skipped to avoid spurious
            // "Already unmounting" / "not mounted" failures during concurrent teardown.
            return this.TryGetRepoMountStatus(repoRoot, out string mountStatus)
                && (mountStatus.Equals(NamedPipeMessages.GetStatus.Ready, StringComparison.Ordinal)
                    || mountStatus.Equals(NamedPipeMessages.GetStatus.MountFailed, StringComparison.Ordinal));
        }

        private bool TryGetRepoMountStatus(string repoRoot, out string mountStatus)
        {
            mountStatus = null;

            // Hide the output of status
            StringWriter statusOutput = new StringWriter();
            ReturnCode result = this.Execute<StatusVerb>(
                repoRoot,
                verb =>
                {
                    verb.Output = statusOutput;
                });

            if (result != ReturnCode.Success)
            {
                // No live mount process is answering for this repo.
                return false;
            }

            foreach (string line in statusOutput.ToString().Split('\n'))
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith(StatusVerb.MountStatusOutputPrefix, StringComparison.Ordinal))
                {
                    mountStatus = trimmedLine.Substring(StatusVerb.MountStatusOutputPrefix.Length).Trim();
                    return true;
                }
            }

            return false;
        }
    }
}
