using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.Common.Maintenance
{
    public class LooseObjectsStep : GitMaintenanceStep
    {
        private const string LooseObjects = "loose-objects.history";
        private readonly string looseObjectsHistoryPath = null;
        private readonly bool ignoreTimeRestriction = false;

        public LooseObjectsStep(GVFSContext context, bool requireCacheLock, bool ignoreTimeRestriction)
            : this(context, requireCacheLock)
        {
            this.ignoreTimeRestriction = ignoreTimeRestriction;
        }

        public LooseObjectsStep(GVFSContext context, bool requireCacheLock)
            : base(context, gitObjects: null, requireObjectCacheLock: requireCacheLock)
        {
            this.looseObjectsHistoryPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", LooseObjects);         
        }

        public override string Area => "LooseObjectsStep";

        protected override bool PerformMaintenance()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity("LooseObjectsStep", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                try
                {
                    if (!this.ignoreTimeRestriction && !this.EnoughTimeBetweenRuns())
                    {
                        activity.RelatedWarning("Skipping step due to not enough time between runs");
                        return false;
                    }

                    // Verify no git processes are running
                    IEnumerable<int> processIds = this.RunningGitProcessIds();
                    if (processIds.Count() > 0)
                    {                            
                        activity.RelatedWarning("Skipping LooseObjectsStep due to git pids {0}", string.Join(",", processIds));
                        return false;
                    }

                    // Perform prune-packed
                    int beforeCount = this.CountLooseObjects();
                    GitProcess.Result result = this.RunGitCommand((process) => process.PrunePacked());
                    int afterCount = this.CountLooseObjects();

                    // Record Telemetry
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("StartingCount", beforeCount);
                    metadata.Add("EndingCount", afterCount);
                    metadata.Add("RemovedCount", afterCount - beforeCount);
                    activity.RelatedEvent(EventLevel.Informational, "LooseObjects", metadata, Keywords.Telemetry);

                    // Update the history file
                    this.RecordLooseObjectHistory();
                }
                catch (Exception e)
                {
                    activity.RelatedError("Failed to run LooseObjectsStep: {0}", e.ToString());
                    return false;
                }

                return true;
            }
        }

        private int CountLooseObjects()
        {
            int count = 0;
            int pathLength = this.Context.Enlistment.GitObjectsRoot.Length;

            foreach (string directory in Directory.GetDirectories(this.Context.Enlistment.GitObjectsRoot))
            {
                // Check if the directory is 2 letter HEX
                if (System.Text.RegularExpressions.Regex.IsMatch(directory.Remove(0, pathLength + 1), @"[0-9a-fA-F]{2}"))
                {
                    count += Directory.GetFiles(directory).Count();
                }
            }

            return count;
        }

        // Job should be run 7 days apart
        private bool EnoughTimeBetweenRuns()
        {
            if (!File.Exists(this.looseObjectsHistoryPath))
            {
                return true;
            }

            string lastRunTime = this.Context.FileSystem.ReadAllText(this.looseObjectsHistoryPath);
            DateTime dateTime = Convert.ToDateTime(lastRunTime);
            double daysDiff = (DateTime.Now - dateTime).TotalDays;
            if (daysDiff >= 7)
            {
                return true;
            }

            return false;
        }

        private IEnumerable<int> RunningGitProcessIds()
        {
            Process[] allProcesses = Process.GetProcesses();
            return allProcesses.Where(x => x.ProcessName == "git").Select(x => x.Id);
        }

        private void RecordLooseObjectHistory()
        {
            this.Context.FileSystem.WriteAllText(this.looseObjectsHistoryPath, DateTime.Now.ToString());
        }
    }
}
