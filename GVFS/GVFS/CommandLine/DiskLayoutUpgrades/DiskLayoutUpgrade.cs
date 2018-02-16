using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine.DiskLayoutUpgrades
{
    public abstract class DiskLayoutUpgrade
    {
        protected const string EsentRepoMetadataName = "RepoMetadata";
        protected const string DiskLayoutEsentVersionKey = "DiskLayoutVersion";

        private static readonly Dictionary<int, DiskLayoutUpgrade> AllUpgrades = new List<DiskLayoutUpgrade>()
        {
            new DiskLayout7to8Upgrade(),
            new DiskLayout8to9Upgrade(),
            new DiskLayout9to10Upgrade(),
            new DiskLayout10to11Upgrade(),
            new DiskLayout11to12Upgrade(),
        }.ToDictionary(
            upgrader => upgrader.SourceLayoutVersion, 
            upgrader => upgrader);

        protected abstract int SourceLayoutVersion { get; }

        public static bool TryRunAllUpgrades(string enlistmentRoot)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "DiskLayoutUpgrade"))
            {
                try
                {
                    DiskLayoutUpgrade upgrade = null;
                    while (TryFindUpgrade(tracer, enlistmentRoot, out upgrade))
                    {
                        if (upgrade == null)
                        {
                            return true;
                        }

                        if (!upgrade.TryUpgrade(tracer, enlistmentRoot))
                        {
                            return false;
                        }

                        if (!CheckLayoutVersionWasIncremented(tracer, enlistmentRoot, upgrade))
                        {
                            return false;
                        }
                    }

                    return false;
                }
                catch (Exception e)
                {
                    StartLogFile(enlistmentRoot, tracer);
                    tracer.RelatedError(e.ToString());
                    return false;
                }
                finally
                {
                    RepoMetadata.Shutdown();
                }
            }
        }

        public static bool TryCheckDiskLayoutVersion(ITracer tracer, string enlistmentRoot, out string error)
        {
            error = string.Empty;
            int persistedVersionNumber;
            try
            {
                if (TryGetDiskLayoutVersion(tracer, enlistmentRoot, out persistedVersionNumber, out error))
                {
                    if (persistedVersionNumber < RepoMetadata.DiskLayoutVersion.MinDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Breaking change to GVFS disk layout has been made since cloning. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1} \r\nMinimum supported version: {2}",
                            persistedVersionNumber,
                            RepoMetadata.DiskLayoutVersion.CurrentDiskLayoutVersion,
                            RepoMetadata.DiskLayoutVersion.MinDiskLayoutVersion);

                        return false;
                    }
                    else if (persistedVersionNumber > RepoMetadata.DiskLayoutVersion.MaxDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Changes to GVFS disk layout do not allow mounting after downgrade. Try mounting again using a more recent version of GVFS. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            persistedVersionNumber,
                            RepoMetadata.DiskLayoutVersion.CurrentDiskLayoutVersion);

                        return false;
                    }
                    else if (persistedVersionNumber != RepoMetadata.DiskLayoutVersion.CurrentDiskLayoutVersion)
                    {
                        error = string.Format(
                            "GVFS disk layout version doesn't match current version. Try running 'gvfs mount' to upgrade. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            persistedVersionNumber,
                            RepoMetadata.DiskLayoutVersion.CurrentDiskLayoutVersion);

                        return false;
                    }

                    return true;
                }
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            error = "Failed to read disk layout version. " + ConsoleHelper.GetGVFSLogMessage(enlistmentRoot);
            return false;
        }

        public abstract bool TryUpgrade(ITracer tracer, string enlistmentRoot);

        protected bool TryDeleteFolder(ITracer tracer, string folderName)
        {
            try
            {
                PhysicalFileSystem.RecursiveDelete(folderName);
            }
            catch (Exception e)
            {
                tracer.RelatedError("Failed to delete folder {0}: {1}", folderName, e.ToString());
                return true;
            }

            return true;
        }

        protected bool TryDeleteFile(ITracer tracer, string fileName)
        {
            try
            {
                File.Delete(fileName);
            }
            catch (Exception e)
            {
                tracer.RelatedError("Failed to delete file {0}: {1}", fileName, e.ToString());
                return true;
            }

            return true;
        }

        protected bool TryRenameFolderForDelete(ITracer tracer, string folderName, out string backupFolder)
        {
            backupFolder = folderName + ".deleteme";

            tracer.RelatedInfo("Moving " + folderName + " to " + backupFolder);

            try
            {
                Directory.Move(folderName, backupFolder);
            }
            catch (Exception e)
            {
                tracer.RelatedError("Failed to move {0} to {1}: {2}", folderName, backupFolder, e.ToString());
                return false;
            }

            return true;
        }

        protected bool TryIncrementDiskLayoutVersion(ITracer tracer, string enlistmentRoot, DiskLayoutUpgrade upgrade)
        {
            string newVersion = (upgrade.SourceLayoutVersion + 1).ToString();
            string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);
            string error;
            if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
            {
                tracer.RelatedError("Could not initialize repo metadata: " + error);
                return false;
            }

            RepoMetadata.Instance.SetEntry(RepoMetadata.Keys.DiskLayoutVersion, newVersion);
            tracer.RelatedInfo("Disk layout version is now: " + newVersion);
            return true;
        }

        private static bool CheckLayoutVersionWasIncremented(JsonEtwTracer tracer, string enlistmentRoot, DiskLayoutUpgrade upgrade)
        {
            string error;
            int actualVersion;
            if (!TryGetDiskLayoutVersion(tracer, enlistmentRoot, out actualVersion, out error))
            {
                tracer.RelatedError(error);
                return false;
            }

            int expectedVersion = upgrade.SourceLayoutVersion + 1;
            if (actualVersion != expectedVersion)
            {
                throw new InvalidDataException(string.Format("Disk layout upgrade did not increment layout version. Expected: {0}, Actual: {1}", expectedVersion, actualVersion));
            }

            return true;
        }

        private static bool TryFindUpgrade(JsonEtwTracer tracer, string enlistmentRoot, out DiskLayoutUpgrade upgrade)
        {
            int version;
            string error;
            if (!TryGetDiskLayoutVersion(tracer, enlistmentRoot, out version, out error))
            {
                StartLogFile(enlistmentRoot, tracer);
                tracer.RelatedError(error);
                upgrade = null;
                return false;
            }

            if (AllUpgrades.TryGetValue(version, out upgrade))
            {
                StartLogFile(enlistmentRoot, tracer);
                tracer.RelatedInfo("Upgrading from disk layout {0} to {1}", version, version + 1);
                return true;
            }
                        
            return true;
        }

        private static bool TryGetDiskLayoutVersion(ITracer tracer, string enlistmentRoot, out int version, out string error)
        {
            string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);
            string repoMetadataPath = Path.Combine(dotGVFSPath, EsentRepoMetadataName);
            if (Directory.Exists(repoMetadataPath))
            {
                try
                {
                    using (PersistentDictionary<string, string> oldMetadata = new PersistentDictionary<string, string>(repoMetadataPath))
                    {
                        string versionString = oldMetadata[DiskLayoutEsentVersionKey];
                        if (!int.TryParse(versionString, out version))
                        {
                            error = "Could not parse version string as integer: " + versionString;
                            return false;
                        }
                    }
                }
                catch (Exception e)
                {
                    version = 0;
                    error = e.ToString();
                    return false;
                }
            }
            else
            {
                if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
                {
                    version = 0;
                    return false;
                }

                if (!RepoMetadata.Instance.TryGetOnDiskLayoutVersion(out version, out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void StartLogFile(string enlistmentRoot, JsonEtwTracer tracer)
        {
            if (!tracer.HasLogFileEventListener)
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(
                        Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.LogPath),
                        GVFSConstants.LogFileTypes.Upgrade),
                    EventLevel.Informational,
                    Keywords.Any);

                tracer.WriteStartEvent(enlistmentRoot, repoUrl: "N/A", cacheServerUrl: "N/A");
            }
        }
    }
}
