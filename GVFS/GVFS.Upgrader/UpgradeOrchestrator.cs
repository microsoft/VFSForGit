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

        private IProductUpgrader upgrader;
        private ITracer tracer;
        private InstallerPreRunChecker preRunChecker;
        private TextWriter output;
        private TextReader input;
        private bool mount;

        public UpgradeOrchestrator(
            IProductUpgrader upgrader,
            ITracer tracer,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.preRunChecker = preRunChecker;
            this.output = output;
            this.input = input;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
        }

        public UpgradeOrchestrator()
        {
            string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                ProductUpgraderInfo.GetLogDirectoryPath(),
                GVFSConstants.LogFileTypes.UpgradeProcess);
            JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeProcess");
            jsonTracer.AddLogFileEventListener(
                logFilePath,
                DefaultEventLevel,
                Keywords.Any);

            this.tracer = jsonTracer;
            this.preRunChecker = new InstallerPreRunChecker(this.tracer, GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm);
            this.output = Console.Out;
            this.input = Console.In;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
        }

        public ReturnCode ExitCode { get; private set; }

        public void Execute()
        {
            string error = null;
            string mountError = null;
            Version newVersion = null;

            if (this.TryInitialize(out error))
            {
                try
                {
                    if (!this.TryRunUpgrade(out newVersion, out error))
                    {
                        this.ExitCode = ReturnCode.GenericError;
                    }
                }
                finally
                {
                    if (!this.TryMountRepositories(out mountError))
                    {
                        mountError = Environment.NewLine + "WARNING: " + mountError;
                        this.output.WriteLine(mountError);
                    }

                    this.DeletedDownloadedAssets();
                }
            }
            else
            {
                this.ExitCode = ReturnCode.GenericError;
            }

            if (this.ExitCode == ReturnCode.GenericError)
            {
                error = Environment.NewLine + "ERROR: " + error;
                this.output.WriteLine(error);
            }
            else
            {
                if (newVersion != null)
                {
                    this.output.WriteLine($"{Environment.NewLine}Upgrade completed successfully{(string.IsNullOrEmpty(mountError) ? "." : ", but one or more repositories will need to be mounted manually.")}");
                }
            }

            if (this.input == Console.In)
            {
                this.output.WriteLine("Press Enter to exit.");
                this.input.ReadLine();
            }

            Environment.ExitCode = (int)this.ExitCode;
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

        private bool TryInitialize(out string errorMessage)
        {
            if (this.upgrader == null)
            {
                IProductUpgrader upgrader;
                if (!ProductUpgraderFactory.TryCreateUpgrader(out upgrader, this.tracer, out errorMessage))
                {
                    return false;
                }

                this.upgrader = upgrader;
            }

            return this.upgrader.TryInitialize(out errorMessage);
        }

        private bool TryRunUpgrade(out Version newVersion, out string consoleError)
        {
            newVersion = null;

            Version newGVFSVersion = null;
            string error = null;
            bool isUpgradeAllowed;
            if (!this.upgrader.TryGetConfigAllowsUpgrade(out isUpgradeAllowed, out error))
            {
                this.upgrader.CleanupDownloadDirectory();
                consoleError = error;
                this.tracer.RelatedError($"{nameof(this.TryRunUpgrade)}: Run upgrade failed. {error}");
                return false;
            }

            if (!isUpgradeAllowed)
            {
                this.upgrader.CleanupDownloadDirectory();
                this.output.WriteLine(error);
                consoleError = null;
                return true;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryCheckIfUpgradeAvailable(out newGVFSVersion, out error))
                    {
                        return false;
                    }

                    this.LogInstalledVersionInfo();

                    if (!this.preRunChecker.TryRunPreUpgradeChecks(out error))
                    {
                        return false;
                    }

                    if (!this.TryDownloadUpgrade(newGVFSVersion, out error))
                    {
                        return false;
                    }

                    return true;
                },
                "Downloading"))
            {
                consoleError = error;
                return false;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.preRunChecker.TryUnmountAllGVFSRepos(out error))
                    {
                        return false;
                    }

                    this.mount = true;

                    return true;
                },
                "Unmounting repositories"))
            {
                consoleError = error;
                return false;
            }

            if (!this.upgrader.TryRunInstaller(this.LaunchInsideSpinner, out consoleError))
            {
                return false;
            }

            newVersion = newGVFSVersion;
            consoleError = null;
            return true;
        }

        private bool TryMountRepositories(out string consoleError)
        {
            string errorMessage = string.Empty;
            if (this.mount && !this.LaunchInsideSpinner(
                () =>
                {
                    string mountError;
                    if (!this.preRunChecker.TryMountAllGVFSRepos(out mountError))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Upgrade Step", nameof(this.TryMountRepositories));
                        metadata.Add("Mount Error", mountError);
                        this.tracer.RelatedError(metadata, $"{nameof(this.preRunChecker.TryMountAllGVFSRepos)} failed.");
                        errorMessage += mountError;
                        return false;
                    }

                    return true;
                },
                "Mounting repositories"))
            {
                consoleError = errorMessage;
                return false;
            }

            consoleError = null;
            return true;
        }

        private void DeletedDownloadedAssets()
        {
            string downloadsCleanupError;
            if (!this.upgrader.TryCleanup(out downloadsCleanupError))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.DeletedDownloadedAssets));
                metadata.Add("Download cleanup error", downloadsCleanupError);
                this.tracer.RelatedError(metadata, $"{nameof(this.DeletedDownloadedAssets)} failed.");
            }
        }

        private bool TryGetNewGitVersion(out GitVersion gitVersion, out string consoleError)
        {
            gitVersion = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryGetNewGitVersion), EventLevel.Informational))
            {
                if (!this.upgrader.TryGetGitVersion(out gitVersion, out consoleError))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryGetNewGitVersion));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetGitVersion)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully read Git version {0}", gitVersion);
            }

            return true;
        }

        private bool TryCheckIfUpgradeAvailable(out Version newestVersion, out string consoleError)
        {
            newestVersion = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckIfUpgradeAvailable), EventLevel.Informational))
            {
                if (!this.upgrader.TryGetNewerVersion(out newestVersion, out consoleError))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryCheckIfUpgradeAvailable));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetNewerVersion)} failed. {consoleError}");
                    return false;
                }

                if (newestVersion == null)
                {
                    consoleError = "Upgrade is not available.";
                    this.tracer.RelatedInfo("No new upgrade releases available");
                    return false;
                }

                activity.RelatedInfo("Successfully checked for new release. {0}", newestVersion);
            }

            return true;
        }

        private bool TryDownloadUpgrade(Version version, out string consoleError)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Upgrade Step", nameof(this.TryDownloadUpgrade));
            metadata.Add("Version", version.ToString());

            using (ITracer activity = this.tracer.StartActivity($"{nameof(this.TryDownloadUpgrade)}", EventLevel.Informational, metadata))
            {
                if (!this.upgrader.TryDownloadNewestVersion(out consoleError))
                {
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryDownloadNewestVersion)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully downloaded version: " + version.ToString());
            }

            return true;
        }

        private void LogInstalledVersionInfo()
        {
            EventMetadata metadata = new EventMetadata();
            string installedGVFSVersion = ProcessHelper.GetCurrentProcessVersion();
            metadata.Add(nameof(installedGVFSVersion), installedGVFSVersion);

            GitVersion installedGitVersion = null;
            string error = null;
            string gitPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            if (!string.IsNullOrEmpty(gitPath) && GitProcess.TryGetVersion(gitPath, out installedGitVersion, out error))
            {
                metadata.Add(nameof(installedGitVersion), installedGitVersion.ToString());
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "Installed Version", metadata);
        }
    }
}
