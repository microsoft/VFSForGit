using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Upgrader
{
    public class UpgradeOrchestrator
    {
        private const EventLevel DefaultEventLevel = EventLevel.Informational;

        private ProductUpgrader upgrader;
        private ITracer tracer;
        private InstallerPreRunChecker preRunChecker;
        private TextWriter output;
        private TextReader input;
        private bool isLocked;
        private bool remount;
        private bool deleteDownloadedAssets;
        private bool shouldExit;

        public UpgradeOrchestrator(
            ProductUpgrader upgrader, 
            ITracer tracer,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output,
            bool shouldExit)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.preRunChecker = preRunChecker;
            this.output = output;
            this.input = input;
            this.remount = false;
            this.deleteDownloadedAssets = false;
            this.isLocked = false;
            this.shouldExit = shouldExit;
            this.ExitCode = ReturnCode.Success;
        }

        public UpgradeOrchestrator()
        {
            string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                ProductUpgrader.GetLogDirectoryPath(),
                GVFSConstants.LogFileTypes.UpgradeProcess);
            JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeProcess");
            jsonTracer.AddLogFileEventListener(
                logFilePath,
                DefaultEventLevel,
                Keywords.Any);

            this.tracer = jsonTracer;
            this.preRunChecker = new InstallerPreRunChecker(this.tracer);
            this.upgrader = new ProductUpgrader(ProcessHelper.GetCurrentProcessVersion(), this.tracer);
            this.output = Console.Out;
            this.input = Console.In;
            this.remount = false;
            this.deleteDownloadedAssets = false;
            this.isLocked = false;
            this.shouldExit = false;
            this.ExitCode = ReturnCode.Success;
        }

        public ReturnCode ExitCode { get; private set; }

        public void Execute()
        {
            string error = null;
            string finishMessage = null;

            if (this.upgrader.IsNoneRing())
            {
                finishMessage = "Upgrade ring set to None. No upgrade check was performed.";
            }
            else
            {
                try
                {
                    Version newVersion = null;
                    if (!this.TryRunUpgradeInstall(out newVersion, out error))
                    {
                        this.ExitCode = ReturnCode.GenericError;
                    }
                }
                finally
                {
                    string cleanUpError = null;
                    if (!this.TryRunCleanUp(out cleanUpError))
                    {
                        error = string.IsNullOrEmpty(error) ? cleanUpError : error + Environment.NewLine + cleanUpError;
                        this.ExitCode = ReturnCode.GenericError;
                    }
                }

                finishMessage = "Finished upgrade";
            }
            
            if (this.ExitCode == ReturnCode.GenericError)
            {
                finishMessage = "Upgrade finished with errors";
                this.tracer.RelatedInfo(finishMessage);
                this.output.WriteLine(error);
            }

            this.output.WriteLine(finishMessage + ". Press Enter to exit.");
            if (this.input == Console.In)
            {
                this.input.ReadLine();
            }
            
            if (this.shouldExit)
            {
                Environment.Exit((int)this.ExitCode);
            }
        }

        private bool LaunchInsideSpinner(Func<bool> method, string message)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                method,
                message,
                this.output,
                this.output == Console.Out && !GVFSPlatform.Instance.IsConsoleOutputRedirectedToFile(),
                null);
        }

        private bool TryRunUpgradeInstall(out Version newVersion, out string error)
        {
            newVersion = null;
            error = null;

            Version newGVFSVersion = null;
            GitVersion newGitVersion = null;
            string errorMessage = null;
            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryCheckIfUpgradeAvailable(out newGVFSVersion, out errorMessage) ||
                        !this.TryAcquireUpgradeLock(out errorMessage) ||
                        !this.TryGetNewGitVersion(out newGitVersion, out errorMessage))
                    {
                        return false;
                    }

                    this.LogInstalledVersionInfo();
                    this.LogVersionInfo(newGVFSVersion, newGitVersion, "Available Version");
                    
                    if (!this.preRunChecker.TryRunPreUpgradeChecks(newGitVersion, out errorMessage))
                    {
                        return false;
                    }

                    if (!this.preRunChecker.TryUnmountAllGVFSRepos(out errorMessage))
                    {
                        return false;
                    }

                    this.remount = true;

                    if (!this.TryDownloadUpgrade(newGVFSVersion, out errorMessage) ||
                        !this.TryInstallGitUpgrade(newGitVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                "Installing Git"))
            {
                error = errorMessage;
                return false;
            }

            newVersion = newGVFSVersion;

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryInstallGVFSUpgrade(newGVFSVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                "Installing GVFS"))
            {
                newVersion = null;
                error = errorMessage;
                return false;
            }

            this.LogVersionInfo(newGVFSVersion, newGitVersion, "Newly Installed Version");

            return true;
        }
        
        private bool TryRunCleanUp(out string error)
        {
            error = null;

            string errorMessage = string.Empty;
            if (!this.LaunchInsideSpinner(
                () =>
                {
                    bool success = true;
                    string unlockError;
                    if (!this.TryReleaseUpgradeLock(out unlockError))
                    {
                        errorMessage += unlockError + Environment.NewLine;
                        success = false;
                    }

                    string remountError;
                    if (this.remount && !this.preRunChecker.TryMountAllGVFSRepos(out remountError))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Upgrade Step", nameof(this.TryRunCleanUp));
                        this.tracer.RelatedError(metadata, $"{nameof(this.preRunChecker.TryMountAllGVFSRepos)} failed. {remountError}");
                        errorMessage += remountError + Environment.NewLine;
                        success = false;
                    }

                    string downloadsCleanupError;
                    if (this.deleteDownloadedAssets && !this.upgrader.TryCleanup(out downloadsCleanupError))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Upgrade Step", nameof(this.TryRunCleanUp));
                        this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryCleanup)} failed. {downloadsCleanupError}");
                        errorMessage += downloadsCleanupError + Environment.NewLine;
                        success = false;
                    }

                    return success;
                },
                "Finishing upgrade"))
            {
                error = errorMessage.TrimEnd(Environment.NewLine.ToCharArray());
                return false;
            }

            return true;
        }
        
        private bool TryAcquireUpgradeLock(out string error)
        {
            error = null;

            if (this.isLocked)
            {
                error = "Could not acquire global upgrade lock. Already locked.";
                this.tracer.RelatedError($"{nameof(this.TryAcquireUpgradeLock)} failed. {error}");
                return false;
            }

            if (this.upgrader.AcquireUpgradeLock())
            {
                this.tracer.RelatedInfo("Acquired upgrade lock.");
                this.isLocked = true;
                return true;
            }

            error = "Could not acquire global upgrade lock. Another instance of gvfs upgrade might be running.";
            this.tracer.RelatedError($"{nameof(this.TryAcquireUpgradeLock)} failed. {error}");

            return false;
        }

        private bool TryReleaseUpgradeLock(out string error)
        {
            error = null;

            if (this.isLocked && !this.upgrader.ReleaseUpgradeLock())
            {
                error = "Could not release global upgrade lock.";
                this.tracer.RelatedError($"{nameof(this.TryReleaseUpgradeLock)} failed. {error}");
                return false;
            }

            return true;
        }

        private bool TryGetNewGitVersion(out GitVersion gitVersion, out string error)
        {
            gitVersion = null;

            this.tracer.RelatedInfo("Reading Git version from release info");

            if (!this.upgrader.TryGetGitVersion(out gitVersion, out error))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.TryGetNewGitVersion));
                this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetGitVersion)} failed. {error}");
                return false;
            }
            
            this.tracer.RelatedInfo("Successfully read Git version {0}", gitVersion);

            return true;
        }

        private bool TryCheckIfUpgradeAvailable(out Version newestVersion, out string error)
        {
            newestVersion = null;

            this.tracer.RelatedInfo("Checking upgrade server for new releases");

            if (!this.upgrader.TryGetNewerVersion(out newestVersion, out error))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.TryCheckIfUpgradeAvailable));
                this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetNewerVersion)} failed. {error}");
                return false;
            }

            if (newestVersion == null)
            {
                error = "No upgrades available in ring: " + this.upgrader.Ring;
                this.tracer.RelatedInfo("No new upgrade releases available");
                return false;
            }

            this.tracer.RelatedInfo("Successfully checked for new release. {0}", newestVersion);
            
            return true;
        }

        private bool TryDownloadUpgrade(Version version, out string error)
        {
            this.tracer.RelatedInfo("Downloading version: " + version.ToString());

            if (!this.upgrader.TryDownloadNewestVersion(out error))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.TryDownloadUpgrade));
                this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryDownloadNewestVersion)} failed. {error}");
                return false;
            }

            this.deleteDownloadedAssets = true;
            this.tracer.RelatedInfo("Successfully downloaded version: " + version.ToString());

            return true;
        }

        private bool TryInstallGitUpgrade(GitVersion version, out string error)
        {
            this.tracer.RelatedInfo("Installing Git version: " + version.ToString());

            bool installSuccess = false;
            if (!this.upgrader.TryRunGitInstaller(out installSuccess, out error) ||
                !installSuccess)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.TryInstallGitUpgrade));
                this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunGitInstaller)} failed. {error}");
                return false;
            }

            this.tracer.RelatedInfo("Successfully installed Git version: " + version.ToString());

            return installSuccess;
        }

        private bool TryInstallGVFSUpgrade(Version version, out string error)
        {
            this.tracer.RelatedInfo("Installing GVFS version: " + version.ToString());

            bool installSuccess = false;
            if (!this.upgrader.TryRunGVFSInstaller(out installSuccess, out error) ||
                !installSuccess)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.TryInstallGVFSUpgrade));
                this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunGVFSInstaller)} failed. {error}");
                return false;
            }

            this.tracer.RelatedInfo("Successfully installed GVFS version: " + version.ToString());

            return installSuccess;
        }

        private void LogVersionInfo(
            Version gvfsVersion,
            GitVersion gitVersion,
            string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("GVFS", gvfsVersion.ToString());
            metadata.Add("Git", gitVersion.ToString());

            this.tracer.RelatedEvent(EventLevel.Informational, message, metadata);
        }

        private void LogInstalledVersionInfo()
        {
            EventMetadata metadata = new EventMetadata();
            string installedGVFSVersion = ProcessHelper.GetCurrentProcessVersion();
            metadata.Add("GVFS", installedGVFSVersion);

            GitVersion installedGitVersion = null;
            string error = null;
            if (this.preRunChecker.TryGetGitVersion(
                out installedGitVersion,
                out error))
            {
                metadata.Add("Git", installedGitVersion.ToString());
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "Installed Version", metadata);
        }
    }
}