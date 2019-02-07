using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.DiskLayoutUpgrades
{
    public abstract class DiskLayoutUpgrade
    {
        private static Dictionary<int, MajorUpgrade> majorVersionUpgrades;
        private static Dictionary<int, Dictionary<int, MinorUpgrade>> minorVersionUpgrades;
        protected abstract int SourceMajorVersion { get; }
        protected abstract int SourceMinorVersion { get; }
        protected abstract bool IsMajorUpgrade { get; }

        public static bool TryRunAllUpgrades(string enlistmentRoot)
        {
            majorVersionUpgrades = new Dictionary<int, MajorUpgrade>();
            minorVersionUpgrades = new Dictionary<int, Dictionary<int, MinorUpgrade>>();

            foreach (DiskLayoutUpgrade upgrade in GVFSPlatform.Instance.DiskLayoutUpgrade.Upgrades)
            {
                RegisterUpgrade(upgrade);
            }

            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "DiskLayoutUpgrade"))
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
            int majorVersion;
            int minorVersion;
            try
            {
                if (TryGetDiskLayoutVersion(tracer, enlistmentRoot, out majorVersion, out minorVersion, out error))
                {
                    if (majorVersion < RepoMetadata.DiskLayoutVersion.MinimumSupportedMajorVersion)
                    {
                        error = string.Format(
                            "Breaking change to GVFS disk layout has been made since cloning. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1} \r\nMinimum supported version: {2}",
                            majorVersion,
                            RepoMetadata.DiskLayoutVersion.CurrentMajorVersion,
                            RepoMetadata.DiskLayoutVersion.MinimumSupportedMajorVersion);

                        return false;
                    }
                    else if (majorVersion > RepoMetadata.DiskLayoutVersion.CurrentMajorVersion)
                    {
                        error = string.Format(
                            "Changes to GVFS disk layout do not allow mounting after downgrade. Try mounting again using a more recent version of GVFS. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            majorVersion,
                            RepoMetadata.DiskLayoutVersion.CurrentMajorVersion);

                        return false;
                    }
                    else if (majorVersion != RepoMetadata.DiskLayoutVersion.CurrentMajorVersion)
                    {
                        error = string.Format(
                            "GVFS disk layout version doesn't match current version. Try running 'gvfs mount' to upgrade. \r\nEnlistment disk layout version: {0}.{1} \r\nGVFS disk layout version: {2}.{3}",
                            majorVersion,
                            minorVersion,
                            RepoMetadata.DiskLayoutVersion.CurrentMajorVersion,
                            RepoMetadata.DiskLayoutVersion.CurrentMinorVersion);

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
                PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                fileSystem.RecursiveDelete(folderName);
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

        protected bool TrySetGitConfig(ITracer tracer, string enlistmentRoot, Dictionary<string, string> configSettings, out string errorMessage)
        {
            errorMessage = null;

            GVFSEnlistment enlistment = GVFSEnlistment.CreateFromDirectory(
                enlistmentRoot,
                GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(),
                ProcessHelper.GetCurrentProcessLocation(),
                authentication: null);
            GitProcess git = enlistment.CreateGitProcess();

            foreach (string key in configSettings.Keys)
            {
                GitProcess.Result result = git.SetInLocalConfig(key, configSettings[key]);
                if (result.ExitCodeIsFailure)
                {
                    tracer.RelatedError("Could not set git config setting {0}. Error: {1}", key, result.Errors);
                    return false;
                }
            }

            return true;
        }

        private static void RegisterUpgrade(DiskLayoutUpgrade upgrade)
        {
            if (upgrade.IsMajorUpgrade)
            {
                majorVersionUpgrades.Add(upgrade.SourceMajorVersion, (MajorUpgrade)upgrade);
            }
            else
            {
                if (minorVersionUpgrades.ContainsKey(upgrade.SourceMajorVersion))
                {
                    minorVersionUpgrades[upgrade.SourceMajorVersion].Add(upgrade.SourceMinorVersion, (MinorUpgrade)upgrade);
                }
                else
                {
                    minorVersionUpgrades.Add(upgrade.SourceMajorVersion, new Dictionary<int, MinorUpgrade> { { upgrade.SourceMinorVersion, (MinorUpgrade)upgrade } });
                }
            }
        }

        private static bool CheckLayoutVersionWasIncremented(JsonTracer tracer, string enlistmentRoot, DiskLayoutUpgrade upgrade)
        {
            string error;
            int actualMajorVersion;
            int actualMinorVersion;
            if (!TryGetDiskLayoutVersion(tracer, enlistmentRoot, out actualMajorVersion, out actualMinorVersion, out error))
            {
                tracer.RelatedError(error);
                return false;
            }

            int expectedMajorVersion =
                upgrade.IsMajorUpgrade
                ? upgrade.SourceMajorVersion + 1
                : upgrade.SourceMajorVersion;
            int expectedMinorVersion =
                upgrade.IsMajorUpgrade
                ? 0
                : upgrade.SourceMinorVersion + 1;

            if (actualMajorVersion != expectedMajorVersion ||
                actualMinorVersion != expectedMinorVersion)
            {
                throw new InvalidDataException(string.Format(
                    "Disk layout upgrade did not increment layout version. Expected: {0}.{1}, Actual: {2}.{3}",
                    expectedMajorVersion,
                    expectedMinorVersion,
                    actualMajorVersion,
                    actualMinorVersion));
            }

            return true;
        }

        private static bool TryFindUpgrade(JsonTracer tracer, string enlistmentRoot, out DiskLayoutUpgrade upgrade)
        {
            int majorVersion;
            int minorVersion;

            string error;
            if (!TryGetDiskLayoutVersion(tracer, enlistmentRoot, out majorVersion, out minorVersion, out error))
            {
                StartLogFile(enlistmentRoot, tracer);
                tracer.RelatedError(error);
                upgrade = null;
                return false;
            }

            Dictionary<int, MinorUpgrade> minorVersionUpgradesForCurrentMajorVersion;
            if (minorVersionUpgrades.TryGetValue(majorVersion, out minorVersionUpgradesForCurrentMajorVersion))
            {
                MinorUpgrade minorUpgrade;
                if (minorVersionUpgradesForCurrentMajorVersion.TryGetValue(minorVersion, out minorUpgrade))
                {
                    StartLogFile(enlistmentRoot, tracer);
                    tracer.RelatedInfo(
                        "Upgrading from disk layout {0}.{1} to {0}.{2}",
                        majorVersion,
                        minorVersion,
                        minorVersion + 1);

                    upgrade = minorUpgrade;
                    return true;
                }
            }

            MajorUpgrade majorUpgrade;
            if (majorVersionUpgrades.TryGetValue(majorVersion, out majorUpgrade))
            {
                StartLogFile(enlistmentRoot, tracer);
                tracer.RelatedInfo("Upgrading from disk layout {0} to {1}", majorVersion, majorVersion + 1);

                upgrade = majorUpgrade;
                return true;
            }

            // return true to indicate that we succeeded, and no upgrader was found
            upgrade = null;
            return true;
        }

        private static bool TryGetDiskLayoutVersion(
            ITracer tracer,
            string enlistmentRoot,
            out int majorVersion,
            out int minorVersion,
            out string error)
        {
            majorVersion = 0;
            minorVersion = 0;

            string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);

            if (!GVFSPlatform.Instance.DiskLayoutUpgrade.TryParseLegacyDiskLayoutVersion(dotGVFSPath, out majorVersion))
            {
                if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
                {
                    majorVersion = 0;
                    return false;
                }

                if (!RepoMetadata.Instance.TryGetOnDiskLayoutVersion(out majorVersion, out minorVersion, out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static void StartLogFile(string enlistmentRoot, JsonTracer tracer)
        {
            if (!tracer.HasLogFileEventListener)
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(
                        Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.LogPath),
                        GVFSConstants.LogFileTypes.MountUpgrade),
                    EventLevel.Informational,
                    Keywords.Any);

                tracer.WriteStartEvent(enlistmentRoot, repoUrl: "N/A", cacheServerUrl: "N/A");
            }
        }

        public abstract class MajorUpgrade : DiskLayoutUpgrade
        {
            protected sealed override bool IsMajorUpgrade
            {
                get { return true; }
            }

            protected sealed override int SourceMinorVersion
            {
                get { throw new NotSupportedException(); }
            }

            protected bool TryIncrementMajorVersion(ITracer tracer, string enlistmentRoot)
            {
                string newMajorVersion = (this.SourceMajorVersion + 1).ToString();
                string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);
                string error;
                if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
                {
                    tracer.RelatedError("Could not initialize repo metadata: " + error);
                    return false;
                }

                RepoMetadata.Instance.SetEntry(RepoMetadata.Keys.DiskLayoutMajorVersion, newMajorVersion);
                RepoMetadata.Instance.SetEntry(RepoMetadata.Keys.DiskLayoutMinorVersion, "0");

                tracer.RelatedInfo("Disk layout version is now: " + newMajorVersion);
                return true;
            }
        }

        public abstract class MinorUpgrade : DiskLayoutUpgrade
        {
            protected sealed override bool IsMajorUpgrade
            {
                get { return false; }
            }

            protected bool TryIncrementMinorVersion(ITracer tracer, string enlistmentRoot)
            {
                string newMinorVersion = (this.SourceMinorVersion + 1).ToString();
                string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);
                string error;
                if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
                {
                    tracer.RelatedError("Could not initialize repo metadata: " + error);
                    return false;
                }

                RepoMetadata.Instance.SetEntry(RepoMetadata.Keys.DiskLayoutMinorVersion, newMinorVersion);

                tracer.RelatedInfo("Disk layout version is now: {0}.{1}", this.SourceMajorVersion, newMinorVersion);
                return true;
            }
        }
    }
}
