using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.Common.Maintenance
{
    // Performs LooseObject Maintenace
    // 1. Removes loose objects that appear in packfiles
    // 2. Packs loose objects into a packfile
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
        public int LooseObjectsPutIntoPackFile { get; private set; } = 0;
        
        // 50,000 was found to be the optimal time taking ~5 minutes
        public int MaxLooseObjectsInPack { get; set; } = 50000;

        protected override string LastRunTimeFilePath => Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", LooseObjectsLastRunFileName);
        protected override TimeSpan TimeBetweenRuns => TimeSpan.FromDays(1);

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

        public void WriteLooseObjectIds(StreamWriter streamWriter)
        {
            this.LooseObjectsPutIntoPackFile = 0;

            // Find loose Objects
            foreach (DirectoryItemInfo directoryItemInfo in this.Context.FileSystem.ItemsInDirectory(this.Context.Enlistment.GitObjectsRoot))
            {
                if (directoryItemInfo.IsDirectory)
                {
                    string directoryName = directoryItemInfo.Name;

                    if (GitObjects.IsLooseObjectsDirectory(directoryName))
                    {
                        string[] looseObjectFileNamesInDir = this.Context.FileSystem.GetFiles(directoryItemInfo.FullName, "*");

                        foreach (string filePath in looseObjectFileNamesInDir)
                        {
                            if (!this.TryGetLooseObjectId(directoryName, filePath, out string objectId))
                            {
                                this.Context.Tracer.RelatedWarning($"Invalid ObjectId {objectId} using directory {directoryName} and path {filePath}");
                                continue;
                            }

                            this.LooseObjectsPutIntoPackFile++;
                            streamWriter.Write(objectId + "\n");

                            // Stop when we hit the limit.  The next run will pack more files.
                            if (this.LooseObjectsPutIntoPackFile >= this.MaxLooseObjectsInPack)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

        public bool TryGetLooseObjectId(string directoryName, string filePath, out string objectId)
        {
            objectId = directoryName + Path.GetFileName(filePath);
            if (!SHA1Util.IsValidShaFormat(objectId))
            {
                return false;
            }

            return true;
        }

        public void CreateLooseObjectsPackFile()
        {
            GitProcess.Result result = this.RunGitCommand(
                (process) => process.PackObjects(
                    "from-loose",
                    this.Context.Enlistment.GitObjectsRoot,
                    this.WriteLooseObjectIds));
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
                            activity.RelatedWarning($"Skipping {nameof(LooseObjectsStep)} due to git pids {string.Join(",", processIds)}", Keywords.Telemetry);
                            return;
                        }
                    }

                    int beforeLooseObjectsCount = this.CountLooseObjects();
                    GitProcess.Result result = this.RunGitCommand((process) => process.PrunePacked(this.Context.Enlistment.GitObjectsRoot));
                    int afterLooseObjectsCount = this.CountLooseObjects();

                    this.CreateLooseObjectsPackFile();

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("GitObjectsRoot", this.Context.Enlistment.GitObjectsRoot);
                    metadata.Add("StartingCount", beforeLooseObjectsCount);
                    metadata.Add("EndingCount", afterLooseObjectsCount);
                    metadata.Add("RemovedCount", beforeLooseObjectsCount - afterLooseObjectsCount);
                    metadata.Add("LooseObjectsPutIntoPackFile", this.LooseObjectsPutIntoPackFile);
                    activity.RelatedEvent(EventLevel.Informational, $"{this.Area}_{nameof(this.PerformMaintenance)}", metadata, Keywords.Telemetry);
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
