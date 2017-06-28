using CommandLine;
using GVFS.CommandLine.RepairJobs;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
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
        public override string EnlistmentRootPath { get; set; }
        
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
            if (string.IsNullOrWhiteSpace(this.EnlistmentRootPath))
            {
                this.EnlistmentRootPath = Environment.CurrentDirectory;
            }

            GVFSEnlistment enlistment = GVFSEnlistment.CreateWithoutRepoUrlFromDirectory(
                this.EnlistmentRootPath,
                GitProcess.GetInstalledGitBinPath());

            if (enlistment == null)
            {
                this.ReportErrorAndExit("'gvfs repair' must be run within a GVFS enlistment");
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

            if (!ConsoleHelper.ShowStatusWhileRunning(
                () =>
                {
                    return GVFSVerb.Execute<StatusVerb>(enlistment.EnlistmentRoot, verb => verb.Output = new StringWriter()) != ReturnCode.Success;
                },
                "Checking 'gvfs status'",
                this.Output,
                showSpinner: true,
                suppressGvfsLogMessage: true))
            {
                this.ReportErrorAndExit("You can only run 'gvfs repair' if GVFS is not mounted. Run 'gvfs unmount' and try again.");
            }

            this.Output.WriteLine();

            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "RepairVerb"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Repair),
                    EventLevel.Verbose,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    enlistment.CacheServerUrl,
                    new EventMetadata
                    {
                        { "Confirmed", this.Confirmed }
                    });

                List<RepairJob> jobs = new List<RepairJob>();

                // Repair ESENT Databases
                jobs.Add(new BackgroundOperationDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new BlobSizeDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new PlaceholderDatabaseRepairJob(tracer, this.Output, enlistment));
                jobs.Add(new RepoMetadataDatabaseRepairJob(tracer, this.Output, enlistment));

                jobs.Add(new GitHeadRepairJob(tracer, this.Output, enlistment));

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
                        if (job.TryFixIssues(repairMessages))
                        {
                            this.WriteMessage(tracer, "Repair succeeded");
                        }
                        else
                        {
                            this.WriteMessage(tracer, "Repair failed. Run 'gvfs log' for more info.");
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
            tracer.RelatedEvent(EventLevel.Informational, "RepairInfo", new EventMetadata { { "Message", message } });
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
