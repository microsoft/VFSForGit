using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
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

        public LooseObjectsStep(
            GVFSContext context,
            bool requireCacheLock = true,
            bool forceRun = false,
            GitProcessChecker gitProcessChecker = null)
            : base(context, requireCacheLock, gitProcessChecker)
        {
            this.forceRun = forceRun;
        }

        public enum CreatePackResult
        {
            Succeess,
            UnknownFailure,
            CorruptObject
        }

        public override string Area => nameof(LooseObjectsStep);

        // 50,000 was found to be the optimal time taking ~5 minutes
        public int MaxLooseObjectsInPack { get; set; } = 50000;

        protected override string LastRunTimeFilePath => Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", LooseObjectsLastRunFileName);
        protected override TimeSpan TimeBetweenRuns => TimeSpan.FromDays(1);

        public void CountLooseObjects(out int count, out long size)
        {
            count = 0;
            size = 0;

            foreach (string directoryPath in this.Context.FileSystem.EnumerateDirectories(this.Context.Enlistment.GitObjectsRoot))
            {
                string directoryName = directoryPath.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last();

                if (GitObjects.IsLooseObjectsDirectory(directoryName))
                {
                    List<DirectoryItemInfo> dirItems = this.Context.FileSystem.ItemsInDirectory(directoryPath).ToList();
                    count += dirItems.Count;
                    size += dirItems.Sum(item => item.Length);
                }
            }
        }

        public IEnumerable<string> GetBatchOfLooseObjects(int batchSize)
        {
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

                            batchSize--;
                            yield return objectId;

                            if (batchSize <= 0)
                            {
                                yield break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Writes loose object Ids to streamWriter
        /// </summary>
        /// <param name="streamWriter">Writer to which SHAs are written</param>
        /// <returns>The number of loose objects SHAs written to the stream</returns>
        public int WriteLooseObjectIds(StreamWriter streamWriter)
        {
            int count = 0;

            foreach (string objectId in this.GetBatchOfLooseObjects(this.MaxLooseObjectsInPack))
            {
                streamWriter.Write(objectId + "\n");
                count++;
            }

            return count;
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

        /// <summary>
        /// Creates a pack file from loose objects
        /// </summary>
        /// <returns>The number of loose objects added to the pack file</returns>
        public CreatePackResult TryCreateLooseObjectsPackFile(out int objectsAddedToPack)
        {
            int localObjectCount = 0;

            GitProcess.Result result = this.RunGitCommand(
                (process) => process.PackObjects(
                    "from-loose",
                    this.Context.Enlistment.GitObjectsRoot,
                    (StreamWriter writer) => localObjectCount = this.WriteLooseObjectIds(writer)),
                nameof(GitProcess.PackObjects));

            if (result.ExitCodeIsSuccess)
            {
                objectsAddedToPack = localObjectCount;
                return CreatePackResult.Succeess;
            }
            else
            {
                objectsAddedToPack = 0;

                if (result.Errors.Contains("is corrupt"))
                {
                    return CreatePackResult.CorruptObject;
                }

                return CreatePackResult.UnknownFailure;
            }
        }

        public string GetLooseObjectFileName(string objectId)
        {
            return Path.Combine(
                            this.Context.Enlistment.GitObjectsRoot,
                            objectId.Substring(0, 2),
                            objectId.Substring(2, GVFSConstants.ShaStringLength - 2));
        }

        public void ClearCorruptLooseObjects(EventMetadata metadata)
        {
            int numDeletedObjects = 0;
            int numFailedDeletes = 0;

            // Double the batch size to look beyond the current batch for bad objects, as there
            // may be more bad objects in the next batch after deleting the corrupt objects.
            foreach (string objectId in this.GetBatchOfLooseObjects(2 * this.MaxLooseObjectsInPack))
            {
                if (!this.Context.Repository.ObjectExists(objectId))
                {
                    string objectFile = this.GetLooseObjectFileName(objectId);

                    if (this.Context.FileSystem.TryDeleteFile(objectFile))
                    {
                        numDeletedObjects++;
                    }
                    else
                    {
                        numFailedDeletes++;
                    }
                }
            }

            metadata.Add("RemovedCorruptObjects", numDeletedObjects);
            metadata.Add("NumFailedDeletes", numFailedDeletes);
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

                        IEnumerable<int> processIds = this.GitProcessChecker.GetRunningGitProcessIds();
                        if (processIds.Any())
                        {
                            activity.RelatedWarning($"Skipping {nameof(LooseObjectsStep)} due to git pids {string.Join(",", processIds)}", Keywords.Telemetry);
                            return;
                        }
                    }

                    this.CountLooseObjects(out int beforeLooseObjectsCount, out long beforeLooseObjectsSize);
                    this.GetPackFilesInfo(out int beforePackCount, out long beforePackSize, out bool _);

                    GitProcess.Result gitResult = this.RunGitCommand((process) => process.PrunePacked(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.PrunePacked));
                    CreatePackResult createPackResult = this.TryCreateLooseObjectsPackFile(out int objectsAddedToPack);

                    this.CountLooseObjects(out int afterLooseObjectsCount, out long afterLooseObjectsSize);
                    this.GetPackFilesInfo(out int afterPackCount, out long afterPackSize, out bool _);

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("GitObjectsRoot", this.Context.Enlistment.GitObjectsRoot);

                    metadata.Add("PrunedPackedExitCode", gitResult.ExitCode);
                    metadata.Add("StartingCount", beforeLooseObjectsCount);
                    metadata.Add("EndingCount", afterLooseObjectsCount);
                    metadata.Add("StartingPackCount", beforePackCount);
                    metadata.Add("EndingPackCount", afterPackCount);

                    metadata.Add("StartingSize", beforeLooseObjectsSize);
                    metadata.Add("EndingSize", afterLooseObjectsSize);
                    metadata.Add("StartingPackSize", beforePackSize);
                    metadata.Add("EndingPackSize", afterPackSize);

                    metadata.Add("RemovedCount", beforeLooseObjectsCount - afterLooseObjectsCount);
                    metadata.Add("LooseObjectsPutIntoPackFile", objectsAddedToPack);
                    metadata.Add("CreatePackResult", createPackResult.ToString());

                    if (createPackResult == CreatePackResult.CorruptObject)
                    {
                        this.ClearCorruptLooseObjects(metadata);
                    }

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
