using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Text;

namespace GVFS.Upgrader
{
    public abstract class UpgradeOrchestrator
    {
        protected InstallerPreRunChecker preRunChecker;
        protected bool mount;
        protected ITracer tracer;

        private const EventLevel DefaultEventLevel = EventLevel.Informational;

        private ProductUpgrader upgrader;
        private string logDirectory = ProductUpgraderInfo.GetLogDirectoryPath();
        private string installationId;
        private PhysicalFileSystem fileSystem;
        private TextWriter output;
        private TextReader input;

        public UpgradeOrchestrator(
            ProductUpgrader upgrader,
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.preRunChecker = preRunChecker;
            this.output = output;
            this.input = input;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
            this.installationId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        public UpgradeOrchestrator(UpgradeOptions options)
        : this()
        {
            this.DryRun = options.DryRun;
            this.NoVerify = options.NoVerify;
        }

        public UpgradeOrchestrator()
        {
            // CommandLine's Parser will create multiple instances of UpgradeOrchestrator, and we don't want
            // multiple log files to get created.  Defer tracer (and preRunChecker) creation until Execute()
            this.tracer = null;
            this.preRunChecker = null;

            this.fileSystem = new PhysicalFileSystem();
            this.output = Console.Out;
            this.input = Console.In;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
            this.installationId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        public ReturnCode ExitCode { get; private set; }

        public bool DryRun { get; }

        public bool NoVerify { get; }

        public void Execute()
        {
            string error = null;
            string mountError = null;
            Version newVersion = null;

            if (this.tracer == null)
            {
                this.tracer = this.CreateTracer();
            }

            if (this.preRunChecker == null)
            {
                this.preRunChecker = new InstallerPreRunChecker(this.tracer, GVFSPlatform.Instance.Constants.UpgradeConfirmCommandMessage);
            }

            try
            {
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
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine();
                    sb.Append("ERROR: " + error);

                    sb.AppendLine();
                    sb.AppendLine();

                    sb.AppendLine($"Upgrade logs can be found at: {this.logDirectory} with file names that end with the installation ID: {this.installationId}.");

                    this.output.WriteLine(sb.ToString());
                }
                else
                {
                    if (newVersion != null)
                    {
                        this.output.WriteLine($"{Environment.NewLine}Upgrade completed successfully{(string.IsNullOrEmpty(mountError) ? "." : ", but one or more repositories will need to be mounted manually.")}");
                    }
                }
            }
            finally
            {
                this.upgrader?.Dispose();
            }

            if (this.input == Console.In)
            {
                this.output.WriteLine("Press Enter to exit.");
                this.input.ReadLine();
            }

            Environment.ExitCode = (int)this.ExitCode;
        }

        protected bool LaunchInsideSpinner(Func<bool> method, string message)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                method,
                message,
                this.output,
                this.output == Console.Out && !GVFSPlatform.Instance.IsConsoleOutputRedirectedToFile(),
                null);
        }

        protected abstract bool TryMountRepositories(out string consoleError);

        private JsonTracer CreateTracer()
        {
            string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                this.logDirectory,
                GVFSConstants.LogFileTypes.UpgradeProcess,
                logId: null,
                fileSystem: this.fileSystem);

            JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeProcess");

            jsonTracer.AddLogFileEventListener(
                logFilePath,
                DefaultEventLevel,
                Keywords.Any);

            return jsonTracer;
        }

        private bool TryInitialize(out string errorMessage)
        {
            if (this.upgrader == null)
            {
                string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
                if (string.IsNullOrEmpty(gitBinPath))
                {
                    errorMessage = $"nameof(this.TryInitialize): Unable to locate git installation. Ensure git is installed and try again.";
                    return false;
                }

                ICredentialStore credentialStore = new GitProcess(gitBinPath, workingDirectoryRoot: null);

                ProductUpgrader upgrader;
                if (!ProductUpgrader.TryCreateUpgrader(this.tracer, this.fileSystem, new LocalGVFSConfig(), credentialStore, this.DryRun, this.NoVerify, out upgrader, out errorMessage))
                {
                    return false;
                }

                // Configure the upgrader to have installer logs written to the same directory
                // as the upgrader.
                upgrader.UpgradeInstanceId = this.installationId;
                this.upgrader = upgrader;
            }

            errorMessage = null;
            return true;
        }

        private bool TryRunUpgrade(out Version newVersion, out string consoleError)
        {
            Version newGVFSVersion = null;
            string error = null;

            if (!this.upgrader.UpgradeAllowed(out error))
            {
                ProductUpgraderInfo productUpgraderInfo = new ProductUpgraderInfo(
                    this.tracer,
                    this.fileSystem);
                productUpgraderInfo.DeleteAllInstallerDownloads();
                this.output.WriteLine(error);
                consoleError = null;
                newVersion = null;
                return true;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.preRunChecker.TryRunPreUpgradeChecks(out error))
                    {
                        return false;
                    }

                    if (!this.TryCheckIfUpgradeAvailable(out newGVFSVersion, out error))
                    {
                        return false;
                    }

                    this.LogInstalledVersionInfo();

                    if (newGVFSVersion != null && !this.TryDownloadUpgrade(newGVFSVersion, out error))
                    {
                        return false;
                    }

                    return true;
                },
                "Downloading"))
            {
                newVersion = null;
                consoleError = error;
                return false;
            }

            if (newGVFSVersion == null)
            {
                newVersion = null;
                consoleError = null;
                return true;
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
                newVersion = null;
                consoleError = error;
                return false;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.preRunChecker.IsInstallationBlockedByRunningProcess(out error))
                    {
                        return false;
                    }

                    return true;
                },
                "Checking for blocking processes."))
            {
                newVersion = null;
                consoleError = error;
                return false;
            }

            if (!this.upgrader.TryRunInstaller(this.LaunchInsideSpinner, out consoleError))
            {
                newVersion = null;
                return false;
            }

            newVersion = newGVFSVersion;
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

        private bool TryCheckIfUpgradeAvailable(out Version newestVersion, out string consoleError)
        {
            newestVersion = null;
            consoleError = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckIfUpgradeAvailable), EventLevel.Informational))
            {
                string message;
                if (!this.upgrader.TryQueryNewestVersion(out newestVersion, out message))
                {
                    consoleError = message;
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryCheckIfUpgradeAvailable));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryQueryNewestVersion)} failed. {consoleError}");
                    return false;
                }

                if (newestVersion == null)
                {
                    this.output.WriteLine(message);
                    this.tracer.RelatedInfo($"No new upgrade releases available. {message}");
                    return true;
                }

                activity.RelatedInfo("New release found - latest available version: {0}", newestVersion);
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
