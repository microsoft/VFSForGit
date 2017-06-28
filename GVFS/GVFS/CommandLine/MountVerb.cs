using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(MountVerb.MountVerbName, HelpText = "Mount a GVFS virtual repo")]
    public class MountVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string MountVerbName = "mount";

        [Option(
            'v',
            MountParameters.Verbosity,
            Default = MountParameters.DefaultVerbosity,
            Required = false,
            HelpText = "Sets the verbosity of console logging. Accepts: Verbose, Informational, Warning, Error")]
        public string Verbosity { get; set; }

        [Option(
            'k',
            MountParameters.Keywords,
            Default = MountParameters.DefaultKeywords,
            Required = false,
            HelpText = "A CSV list of logging filter keywords. Accepts: Any, Network")]
        public string KeywordsCsv { get; set; }

        [Option(
            'd',
            MountParameters.DebugWindow,
            Default = false,
            Required = false,
            HelpText = "Show the debug window.  By default, all output is written to a log file and no debug window is shown.")]
        public bool ShowDebugWindow { get; set; }

        public bool SkipMountedCheck { get; set; }
        public bool SkipVersionCheck { get; set; }

        protected override string VerbName
        {
            get { return MountVerbName; }
        }

        public override void InitializeDefaultParameterValues()
        {
            this.Verbosity = MountParameters.DefaultVerbosity;
            this.KeywordsCsv = MountParameters.DefaultKeywords;
        }

        protected override void PreExecute(string enlistmentRootPath)
        {
            this.CheckGVFltRunning();

            if (string.IsNullOrWhiteSpace(enlistmentRootPath))
            {
                enlistmentRootPath = Environment.CurrentDirectory;
            }

            string enlistmentRoot = null;
            if (Directory.Exists(enlistmentRootPath))
            {
                enlistmentRoot = EnlistmentUtils.GetEnlistmentRoot(enlistmentRootPath);
            }

            if (enlistmentRoot == null)
            {
                this.ReportErrorAndExit("Error: '{0}' is not a valid GVFS enlistment", enlistmentRootPath);
            }

            if (!this.SkipMountedCheck)
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(NamedPipeClient.GetPipeNameFromPath(enlistmentRoot)))
                {
                    if (pipeClient.Connect(500))
                    {
                        this.ReportErrorAndExit(ReturnCode.Success, "This repo is already mounted.");
                    }
                }
            }

            bool allowUpgrade = true;
            string error;
            if (!RepoMetadata.CheckDiskLayoutVersion(Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root), allowUpgrade, out error))
            {
                this.ReportErrorAndExit("Error: " + error);
            }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            string errorMessage = null;
            if (!HooksInstallHelper.InstallHooks(enlistment, out errorMessage))
            {
                this.ReportErrorAndExit("Error installing hooks: " + errorMessage);
            }

            if (!enlistment.TryConfigureAlternate(out errorMessage))
            {
                this.ReportErrorAndExit("Error configuring alternate: " + errorMessage);
            }

            if (!this.ShowStatusWhileRunning(
                () => { return this.RequestMount(enlistment, out errorMessage); },
                "Mounting"))
            {
                this.ReportErrorAndExit(errorMessage);
            }
        }

        private bool RequestMount(GVFSEnlistment enlistment, out string errorMessage)
        {
            this.CheckGitVersion(enlistment);
            this.CheckGVFSHooksVersion(enlistment, null);

            if (!this.SkipVersionCheck)
            {
                using (ITracer mountTracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "Mount"))
                {
                    this.CheckVolumeSupportsDeleteNotifications(mountTracer, enlistment);

                    using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(mountTracer, enlistment))
                    {
                        GVFSConfig config = configRequestor.QueryGVFSConfig();
                        this.ValidateGVFSVersion(enlistment, config, mountTracer);
                    }
                }
            }

            // We have to parse these parameters here to make sure they are valid before 
            // handing them to the background process which cannot tell the user when they are bad
            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            GitProcess git = new GitProcess(enlistment);
            if (!git.IsValidRepo())
            {
                errorMessage = "The physical git repo is missing or invalid";
                return false;
            }

            this.SetGitConfigSettings(git);
            return this.SendMountRequest(enlistment, verbosity, keywords, out errorMessage);
        }

        private bool SendMountRequest(GVFSEnlistment enlistment, EventLevel verbosity, Keywords keywords, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.MountRepoRequest request = new NamedPipeMessages.MountRepoRequest();
            request.EnlistmentRoot = enlistment.EnlistmentRoot;
            request.Verbosity = verbosity;
            request.Keywords = keywords;
            request.ShowDebugWindow = this.ShowDebugWindow;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "Unable to mount because GVFS.Service is not responding. Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.MountRepoRequest.Response.Header)
                    {
                        NamedPipeMessages.MountRepoRequest.Response message = NamedPipeMessages.MountRepoRequest.Response.FromMessage(response);

                        if (!string.IsNullOrEmpty(message.ErrorMessage))
                        {
                            errorMessage = message.ErrorMessage;
                            return false;
                        }

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = "Failed to mount GVFS repo.";
                            return false;
                        }
                        else
                        {
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

        private void SetGitConfigSettings(GitProcess git)
        {
            if (!GVFSVerb.TrySetGitConfigSettings(git))
            {
                this.ReportErrorAndExit("Unable to configure git repo");
            }
        }

        private void ParseEnumArgs(out EventLevel verbosity, out Keywords keywords)
        {
            if (!Enum.TryParse(this.KeywordsCsv, out keywords))
            {
                this.ReportErrorAndExit("Error: Invalid logging filter keywords: " + this.KeywordsCsv);
            }

            if (!Enum.TryParse(this.Verbosity, out verbosity))
            {
                this.ReportErrorAndExit("Error: Invalid logging verbosity: " + this.Verbosity);
            }
        }
    }
}