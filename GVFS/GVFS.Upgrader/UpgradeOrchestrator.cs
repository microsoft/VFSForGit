using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Upgrader
{
    [Verb("UpgradeOrchestrator", HelpText = "Checks for product upgrades, downloads and installs it.")]
    public class UpgradeOrchestrator
    {
        private const EventLevel DefaultEventLevel = EventLevel.Informational;

        private ProductUpgrader upgrader;
        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private InstallerPreRunChecker preRunChecker;
        private TextWriter output;
        private TextReader input;
        private bool mount;

        public UpgradeOrchestrator(
            ProductUpgrader upgrader,
            ITracer tracer,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.fileSystem = new PhysicalFileSystem();
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

        [Option(
            "dry-run",
            Default = false,
            Required = false,
            HelpText = "Display progress and errors, but don't install GVFS")]
        public bool DryRun { get; set; }

        [Option(
            "no-verify",
            Default = false,
            Required = false,
            HelpText = "Don't verify authenticode signature of installers")]
        public bool NoVerify { get; set; }

        public void Execute()
        {
            string error = null;
            string mountError = null;
            Version newVersion = null;

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
                ProductUpgrader upgrader;
                if (!ProductUpgrader.TryCreateUpgrader(out upgrader, this.tracer, out errorMessage, this.DryRun, this.NoVerify))
                {
                    return false;
                }

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

            if (!this.upgrader.TryRunInstaller(this.LaunchInsideSpinner, out consoleError))
            {
                newVersion = null;
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
