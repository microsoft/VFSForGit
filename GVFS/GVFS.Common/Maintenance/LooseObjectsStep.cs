using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.Common.Maintenance
{
    // Remove loose objects that appear in packfiles
    public class LooseObjectsStep : GitMaintenanceStep
    {
        public const string LooseObjectsLastRunFileName = "loose-objects.time";
        private readonly bool forceRun;

        public LooseObjectsStep(GVFSContext context, bool requireCacheLock, bool forceRun = false)
            : base(context, gitObjects: null, requireObjectCacheLock: requireCacheLock)
        {
            this.forceRun = forceRun;
        }

        public override string Area => nameof(LooseObjectsStep);

        protected override string LastRunTimeFilePath => Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", LooseObjectsLastRunFileName);
        protected override TimeSpan TimeBetweenRuns => TimeSpan.FromDays(7);

        public int CountLooseObjects()
        {
            int count = 0;
            
            foreach (string directoryPath in this.Context.FileSystem.EnumerateDirectories(this.Context.Enlistment.GitObjectsRoot))
            {
                string directoryName = directoryPath.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last();

                if (GitObjects.IsLooseObjectsDirectory(directoryName))
                {                    
                    count += this.Context.FileSystem.GetFiles(Path.Combine(this.Context.Enlistment.GitObjectsRoot, directoryPath), "*").Count();
                }
            }

            return count;
        }

        protected override void PerformMaintenance()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity(this.Area, EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                try
                {
                    // forceRun is only currently true for functional tests
                    if (!this.forceRun)
                    {
                        if (!this.EnoughTimeBetweenRuns())
                        {
                            activity.RelatedWarning($"Skipping {nameof(LooseObjectsStep)} due to not enough time between runs");
                            return;
                        }

                        IEnumerable<int> processIds = this.RunningGitProcessIds();
                        if (processIds.Any())
                        {
                            activity.RelatedWarning($"Skipping {nameof(LooseObjectsStep)} due to git pids {0}", string.Join(",", processIds), Keywords.Telemetry);
                            return;
                        }
                    }

                    int beforeCount = this.CountLooseObjects();
                    GitProcess.Result result = this.RunGitCommand((process) => process.PrunePacked(this.Context.Enlistment.GitObjectsRoot));
                    int afterCount = this.CountLooseObjects();

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("StartingCount", beforeCount);
                    metadata.Add("EndingCount", afterCount);
                    metadata.Add("RemovedCount", beforeCount - afterCount);
                    activity.RelatedEvent(EventLevel.Informational, this.Area, metadata, Keywords.Telemetry);

                    this.SaveLastRunTimeToFile();
                }
                catch (Exception e)
                {
                    activity.RelatedWarning(this.CreateEventMetadata(e), "Failed to run LooseObjectsStep", Keywords.Telemetry);
                }
            }
        }
    }
}
