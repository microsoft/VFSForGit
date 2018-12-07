using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Common.Tracing;

namespace GVFS.CommandLine
{
    [Verb(MaintenanceVerbName, HelpText = "Runs a maintance job.")]
    public class MaintenanceVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string LooseObjectsJobName = "LooseObjects";
        private const string MaintenanceVerbName = "maintenance";
        private GVFSContext context = null;

        public MaintenanceVerb() : base(false)
        {
        }

        [Option(
            'j',
            "job",
            Required = true,
            HelpText = "Job name to run. Accepts: " + LooseObjectsJobName)]
        public string JobName { get; set; }

        protected override string VerbName => MaintenanceVerbName;

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "MaintenanceVerb"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Maintenance),
                    EventLevel.Informational,
                    Keywords.Any);

                tracer.WriteStartEvent(
                    new EventMetadata
                    {
                        { "EnlistmentRoot", enlistment.EnlistmentRoot }
                    },
                    Keywords.Telemetry);

                this.InitializeGitObjects(tracer, enlistment);
                this.InitializeContext(tracer, enlistment);
 
                // Look up job to run
                GitMaintenanceStep step = null;
                switch (this.JobName)
                {
                    case LooseObjectsJobName:
                        step = new LooseObjectsStep(this.context, requireCacheLock: true, ignoreTimeRestriction: true);
                        break;

                    default:
                        this.Output.WriteLine("Unsupported job name");
                        return;
                }

                // Run job
                this.ShowStatusWhileRunning(
                    () =>
                    {
                        if (!step.Execute())
                        {
                            return false;
                        }

                        return true;
                    },
                    "Running maintenance job");
            }
        }

        private void InitializeGitObjects(ITracer tracer, GVFSEnlistment enlistment)
        {
            string error;
            if (!RepoMetadata.TryInitialize(tracer, enlistment.DotGVFSRoot, out error))
            {
                this.ReportErrorAndExit(tracer, "Failed to load repo metadata: " + error);
            }

            string gitObjectsRoot;
            if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
            {
                this.ReportErrorAndExit(tracer, "Failed to determine git objects root from repo metadata: " + error);
            }

            enlistment.InitializeGitObjects(gitObjectsRoot);
        }

        private void InitializeContext(ITracer tracer, GVFSEnlistment enlistment)
        {
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(
                    tracer,
                    enlistment,
                    fileSystem);
            this.context = new GVFSContext(tracer, fileSystem, gitRepo, enlistment);
        }
    }
}