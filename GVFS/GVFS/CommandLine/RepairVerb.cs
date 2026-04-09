using CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using GVFS.RepairJobs;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(RepairVerb.RepairVerbName, HelpText = "EXPERIMENTAL FEATURE - Repair issues that prevent a GVFS repo from mounting")]
    public class RepairVerb : GVFSVerb
    {
        private const string RepairVerbName = "repair";

        [Value(
            1,
            Required = false,
            Default = "",
            MetaName = "Enlistment Root Path",
            HelpText = "Full or relative path to the GVFS enlistment root")]
        public override string EnlistmentRootPathParameter { get; set; }

        [Option(
            "confirm",
            Default = false,
            Required = false,
            HelpText = "Pass in this flag to actually do repair(s). Without it, only validation will be done.")]
        public bool Confirmed { get; set; }

        protected override string VerbName
        {
            get { return RepairVerb.RepairVerbName; }
        }

        public override void Execute()
        {
            this.ValidatePathParameter(this.EnlistmentRootPathParameter);

            this.CheckGVFSHooksVersion(tracer: null, hooksVersion: out _);

            if (!Directory.Exists(this.EnlistmentRootPathParameter))
            {
                this.ReportErrorAndExit($"Path '{this.EnlistmentRootPathParameter}' does not exist");
            }

            string errorMessage;
            string enlistmentRoot;
            if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(this.EnlistmentRootPathParameter, out enlistmentRoot, out errorMessage))
            {
                this.ReportErrorAndExit("'gvfs repair' must be run within a GVFS enlistment");
            }

            GVFSEnlistment enlistment = null;

            try
            {
                enlistment = GVFSEnlistment.CreateFromDirectory(
                    this.EnlistmentRootPathParameter,
                    GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(),
                    authentication: null,
                    createWithoutRepoURL: true);
            }
            catch (InvalidRepoException e)
            {
                this.ReportErrorAndExit($"Failed to initialize enlistment, error: {e.Message}");
            }

            if (!this.Confirmed)
            {
                this.Output.WriteLine(
@"WARNING: THIS IS AN EXPERIMENTAL FEATURE

This command detects and repairs issues that prevent a GVFS repo from mounting.
A few such checks are currently implemented, and some of them can be repaired.
More repairs and more checks are coming soon.

Without --confirm, it will non-invasively check if repairs are necessary.
To actually execute any necessary repair(s), run 'gvfs repair --confirm'
");
            }

            string error;
            if (!DiskLayoutUpgrade.TryCheckDiskLayoutVersion(tracer: null, enlistmentRoot: enlistment.EnlistmentRoot, error: out error))
            {
                this.ReportErrorAndExit(error);
            }

            if (!ConsoleHelper.ShowStatusWhileRunning(
                () =>
                {
                    // Don't use 'gvfs status' here. The repo may be corrupt such that 'gvfs status' cannot run normally,
                    // causing repair to continue when it shouldn't.
                    using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                    {
                        if (!pipeClient.Connect())
                        {
                            return true;
                        }
                    }

                    return false;
                },
                "Checking that GVFS is not mounted",
                this.Output,
                showSpinner: true,
                gvfsLogEnlistmentRoot: null))
            {
                this.ReportErrorAndExit("You can only run 'gvfs repair' if GVFS is not mounted. Run 'gvfs unmount' and try again.");
            }

            this.Output.WriteLine();

            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "RepairVerb", enlistment.GetEnlistmentId(), mountId: null))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Repair),
                    EventLevel.Verbose,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    "N/A",
                    new EventMetadata
                    {
                        { "Confirmed", this.Confirmed },
                        { "IsElevated", GVFSPlatform.Instance.IsElevated() },
                        { "NamedPipename", enlistment.NamedPipeName },
                        { nameof(this.EnlistmentRootPathParameter), this.EnlistmentRootPathParameter },
                    });

                List<RepairJob> jobs = new List<RepairJob>();

                // Repair databases
                jobs.Add(new BackgroundOperationDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new RepoMetadataDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new VFSForGitDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new BlobSizeDatabaseRepairJob(tracer, this.Output, enlistment));

                // Repair .git folder files
                jobs.Add(new GitHeadRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new GitIndexRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new GitConfigRepairJob(tracer, this.Output, enlistment));

                Dictionary<RepairJob, List<string>> healthy = new Dictionary<RepairJob, List<string>>();
                Dictionary<RepairJob, List<string>> cantFix = new Dictionary<RepairJob, List<string>>();
                Dictionary<RepairJob, List<string>> fixable = new Dictionary<RepairJob, List<string>>();

                foreach (RepairJob job in jobs)
                {
                    List<string> messages = new List<string>();
                    switch (job.HasIssue(messages))
                    {
                        case RepairJob.IssueType.None:
                            healthy[job] = messages;
                            break;

                        case RepairJob.IssueType.CantFix:
                            cantFix[job] = messages;
                            break;

                        case RepairJob.IssueType.Fixable:
                            fixable[job] = messages;
                            break;
                    }
                }

                foreach (RepairJob job in healthy.Keys)
                {
                    this.WriteMessage(tracer, string.Format("{0, -30}: Healthy", job.Name));
                    this.WriteMessages(tracer, healthy[job]);
                }

                if (healthy.Count > 0)
                {
                    this.Output.WriteLine();
                }

                foreach (RepairJob job in cantFix.Keys)
                {
                    this.WriteMessage(tracer, job.Name);
                    this.WriteMessages(tracer, cantFix[job]);
                    this.Indent();
                    this.WriteMessage(tracer, "'gvfs repair' does not currently support fixing this problem");
                    this.Output.WriteLine();
                }

                foreach (RepairJob job in fixable.Keys)
                {
                    this.WriteMessage(tracer, job.Name);
                    this.WriteMessages(tracer, fixable[job]);
                    this.Indent();

                    if (this.Confirmed)
                    {
                        List<string> repairMessages = new List<string>();
                        switch (job.TryFixIssues(repairMessages))
                        {
                            case RepairJob.FixResult.Success:
                                this.WriteMessage(tracer, "Repair succeeded");
                                break;
                            case RepairJob.FixResult.ManualStepsRequired:
                                this.WriteMessage(tracer, "Repair succeeded, but requires some manual steps before remounting.");
                                break;
                            case RepairJob.FixResult.Failure:
                                this.WriteMessage(tracer, "Repair failed. " + ConsoleHelper.GetGVFSLogMessage(enlistment.EnlistmentRoot));
                                break;
                        }

                        this.WriteMessages(tracer, repairMessages);
                    }
                    else
                    {
                        this.WriteMessage(tracer, "Run 'gvfs repair --confirm' to attempt a repair");
                    }

                    this.Output.WriteLine();
                }
            }
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            tracer.RelatedEvent(EventLevel.Informational, "RepairInfo", new EventMetadata { { TracingConstants.MessageKey.InfoMessage, message } });
            this.Output.WriteLine(message);
        }

        private void WriteMessages(ITracer tracer, List<string> messages)
        {
            foreach (string message in messages)
            {
                this.Indent();
                this.WriteMessage(tracer, message);
            }
        }

        private void Indent()
        {
            this.Output.Write("    ");
        }
    }
}
