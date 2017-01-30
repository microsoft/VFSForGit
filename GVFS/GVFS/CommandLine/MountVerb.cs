using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Threading;

namespace GVFS.CommandLine
{
    [Verb(MountVerb.MountVerbName, HelpText = "Mount a GVFS virtual repo")]
    public class MountVerb : GVFSVerb.ForExistingEnlistment
    {
        public const string MountVerbName = "mount";
        private const string MountExeName = "GVFS.Mount.exe";

        private const int BackgroundProcessConnectTimeoutMS = 15000;
        private const int MutexMaxWaitTimeMS = 500;

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
        
        protected override string VerbName
        {
            get { return MountVerbName; }
        }

        public override void InitializeDefaultParameterValues()
        {
            this.Verbosity = MountParameters.DefaultVerbosity;
            this.KeywordsCsv = MountParameters.DefaultKeywords;
        }

        protected override void PreExecute(string enlistmentRootPath, ITracer tracer = null)
        {
            this.Output.WriteLine("Validating repo for mount");
            this.CheckElevated();
            this.CheckGVFltRunning();

            if (string.IsNullOrWhiteSpace(enlistmentRootPath))
            {
                enlistmentRootPath = Environment.CurrentDirectory;
            }

            string enlistmentRoot = GVFSEnlistment.GetEnlistmentRoot(enlistmentRootPath);

            if (enlistmentRoot == null)
            {
                this.ReportErrorAndExit("Error: '{0}' is not a valid GVFS enlistment", enlistmentRootPath);
            }

            using (NamedPipeClient pipeClient = new NamedPipeClient(GVFSEnlistment.GetNamedPipeName(enlistmentRoot)))
            {
                if (pipeClient.Connect(500))
                {
                    this.ReportErrorAndExit("This repo is already mounted.  Try running 'gvfs status'.");
                }
            }

            string error;
            if (!RepoMetadata.CheckDiskLayoutVersion(Path.Combine(enlistmentRoot, GVFSConstants.DotGVFSPath), out error))
            {
                this.ReportErrorAndExit("Error: " + error);
            }            
        }

        protected override void Execute(GVFSEnlistment enlistment, ITracer tracer = null)
        {
            this.CheckGitVersion(enlistment);
            this.CheckAntiVirusExclusion(enlistment);

            string mountExeLocation = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), MountExeName);
            if (!File.Exists(mountExeLocation))
            {
                this.ReportErrorAndExit("Could not find GVFS.Mount.exe. You may need to reinstall GVFS.");
            }

            // This tracer is only needed for the HttpGitObjects so we can check the GVFS version.
            // If we keep it around longer, it will collide with the background process tracer.
            using (ITracer mountTracer = tracer ?? new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "Mount"))
            {
                HttpGitObjects gitObjects = new HttpGitObjects(mountTracer, enlistment, maxConnections: 1);
                this.ValidateGVFSVersion(enlistment, gitObjects, mountTracer);
            }

            // We have to parse these parameters here to make sure they are valid before 
            // handing them to the background process which cannot tell the user when they are bad
            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            GitProcess git = new GitProcess(enlistment);
            if (!git.IsValidRepo())
            {
                this.ReportErrorAndExit("The physical git repo is missing or invalid");
            }

            this.SetGitConfigSettings(git);

            const string ParamPrefix = "--";
            ProcessHelper.StartBackgroundProcess(
                mountExeLocation,
                string.Join(
                    " ",
                    enlistment.EnlistmentRoot,
                    ParamPrefix + MountParameters.Verbosity,
                    this.Verbosity,
                    ParamPrefix + MountParameters.Keywords,
                    this.KeywordsCsv,
                    this.ShowDebugWindow ? ParamPrefix + MountParameters.DebugWindow : string.Empty),
                createWindow: this.ShowDebugWindow);

            this.Output.WriteLine("Waiting for GVFS to mount");

            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect(BackgroundProcessConnectTimeoutMS))
                {
                    this.ReportErrorAndExit("Unable to mount because the background process is not responding.");
                }

                bool isMounted = false;
                int tryCount = 0;
                while (!isMounted)
                {
                    try
                    {
                        pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                        NamedPipeMessages.GetStatus.Response getStatusResponse =
                            NamedPipeMessages.GetStatus.Response.FromJson(pipeClient.ReadRawResponse());

                        if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.Ready)
                        {
                            this.Output.WriteLine("Virtual repo is ready.");
                            isMounted = true;
                        }
                        else if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.MountFailed)
                        {
                            this.ReportErrorAndExit("Failed to mount, run 'gvfs log' for details");
                        }
                        else
                        {
                            if (tryCount % 10 == 0)
                            {
                                this.Output.WriteLine(getStatusResponse.MountStatus + "...");
                            }

                            Thread.Sleep(500);
                            tryCount++;
                        }
                    }
                    catch (BrokenPipeException)
                    {
                        this.ReportErrorAndExit("Failed to mount, run 'gvfs log' for details");
                    }
                }
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

        private void SetGitConfigSettings(GitProcess git)
        {
            if (!GVFSVerb.TrySetGitConfigSettings(git))
            {
                this.ReportErrorAndExit("Unable to configure git repo");
            }
        }
    }
}