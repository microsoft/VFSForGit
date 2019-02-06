using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(UpgradeVerbName, HelpText = "Checks for new GVFS release, downloads and installs it when available.")]
    public class UpgradeVerb : GVFSVerb.ForNoEnlistment
    {
        private const string UpgradeVerbName = "upgrade";
        private const string DryRunOption = "--dry-run";
        private const string NoVerifyOption = "--no-verify";
        private const string ConfirmOption = "--confirm";

        private ITracer tracer;
        private IProductUpgrader upgrader;
        private InstallerPreRunChecker prerunChecker;
        private ProcessLauncher processLauncher;

        public UpgradeVerb(
            IProductUpgrader upgrader,
            ITracer tracer,
            InstallerPreRunChecker prerunChecker,
            ProcessLauncher processWrapper,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.prerunChecker = prerunChecker;
            this.processLauncher = processWrapper;
            this.Output = output;
        }

        public UpgradeVerb()
        {
            this.processLauncher = new ProcessLauncher();
            this.Output = Console.Out;
        }

        [Option(
            "confirm",
            Default = false,
            Required = false,
            HelpText = "Pass in this flag to actually install the newest release")]
        public bool Confirmed { get; set; }

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
            HelpText = "This parameter is reserved for internal use.")]
        public bool NoVerify { get; set; }

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }

        public override void Execute()
        {
            string error;
            if (!this.TryInitializeUpgrader(out error) || !this.TryRunProductUpgrade())
            {
                this.ReportErrorAndExit(this.tracer, ReturnCode.GenericError, error);
            }
        }

        private bool TryInitializeUpgrader(out string error)
        {
            if (this.DryRun && this.Confirmed)
            {
                error = $"{DryRunOption} and {ConfirmOption} arguments are not compatible.";
                return false;
            }

            if (GVFSPlatform.Instance.UnderConstruction.SupportsGVFSUpgrade)
            {
                error = null;
                if (this.upgrader == null)
                {
                    JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeVerb");
                    string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                        ProductUpgraderInfo.GetLogDirectoryPath(),
                        GVFSConstants.LogFileTypes.UpgradeVerb);
                    jsonTracer.AddLogFileEventListener(logFilePath, EventLevel.Informational, Keywords.Any);

                    this.tracer = jsonTracer;
                    this.prerunChecker = new InstallerPreRunChecker(this.tracer, this.Confirmed ? GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm : GVFSConstants.UpgradeVerbMessages.GVFSUpgrade);

                    IProductUpgrader upgrader;
                    if (ProductUpgraderFactory.TryCreateUpgrader(out upgrader, this.tracer, out error, this.DryRun, this.NoVerify))
                    {
                        this.upgrader = upgrader;
                    }
                    else
                    {
                        error = $"ERROR: {error}";
                    }
                }

                return this.upgrader != null;
            }
            else
            {
                error = $"ERROR: {GVFSConstants.UpgradeVerbMessages.GVFSUpgrade} is not supported on this operating system.";
                return false;
            }
        }

        private bool TryRunProductUpgrade()
        {
            string errorOutputFormat = Environment.NewLine + "ERROR: {0}";
            string message = null;
            string cannotInstallReason = null;
            Version newestVersion = null;

            bool isInstallable = this.TryCheckUpgradeInstallable(out cannotInstallReason);
            if (this.ShouldRunUpgraderTool() && !isInstallable)
            {
                this.ReportInfoToConsole($"Cannot upgrade GVFS on this machine.");
                this.Output.WriteLine(errorOutputFormat, cannotInstallReason);
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade is not installable. {cannotInstallReason}");
                return false;
            }

            if (!this.upgrader.UpgradeAllowed(out message))
            {
                ProductUpgraderInfo productUpgraderInfo = new ProductUpgraderInfo(
                    this.tracer,
                    new PhysicalFileSystem());
                productUpgraderInfo.DeleteAllInstallerDownloads();
                this.ReportInfoToConsole(message);
                return true;
            }

            if (!this.TryRunUpgradeChecks(out newestVersion, out message))
            {
                this.Output.WriteLine(errorOutputFormat, message);
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade checks failed. {message}");
                return false;
            }

            if (newestVersion == null)
            {
                // Make sure there a no asset installers remaining in the Downloads directory. This can happen if user
                // upgraded by manually downloading and running asset installers.
                ProductUpgraderInfo productUpgraderInfo = new ProductUpgraderInfo(
                    this.tracer,
                    new PhysicalFileSystem());
                productUpgraderInfo.DeleteAllInstallerDownloads();
                this.ReportInfoToConsole(message);
                return true;
            }

            if (this.ShouldRunUpgraderTool())
            {
                this.ReportInfoToConsole(message);

                if (!isInstallable)
                {
                    this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: {message}");
                    this.Output.WriteLine(errorOutputFormat, message);
                    return false;
                }

                if (!this.TryRunInstaller(out message))
                {
                    this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Could not launch upgrade tool. {message}");
                    this.Output.WriteLine(errorOutputFormat, "Could not launch upgrade tool. " + message);
                    return false;
                }
            }
            else
            {
                string advisoryMessage = string.Join(
                        Environment.NewLine,
                        GVFSConstants.UpgradeVerbMessages.UnmountRepoWarning,
                        GVFSConstants.UpgradeVerbMessages.UpgradeInstallAdvice);
                this.ReportInfoToConsole(message + Environment.NewLine + Environment.NewLine + advisoryMessage + Environment.NewLine);
            }

            return true;
        }

        private bool TryRunUpgradeChecks(
            out Version latestVersion,
            out string error)
        {
            bool upgradeCheckSuccess = false;
            string errorMessage = null;
            Version version = null;

            this.ShowStatusWhileRunning(
                () =>
                {
                    upgradeCheckSuccess = this.TryCheckUpgradeAvailable(out version, out errorMessage);
                    return upgradeCheckSuccess;
                },
                 "Checking for GVFS upgrades",
                suppressGvfsLogMessage: true);

            latestVersion = version;
            error = errorMessage;

            return upgradeCheckSuccess;
        }

        private bool TryRunInstaller(out string consoleError)
        {
            string upgraderPath = null;
            string errorMessage = null;

            this.ReportInfoToConsole("Launching upgrade tool...");

            if (!this.TryCopyUpgradeTool(out upgraderPath, out consoleError))
            {
                return false;
            }

            if (!this.TryLaunchUpgradeTool(upgraderPath, out errorMessage))
            {
                return false;
            }

            this.ReportInfoToConsole($"{Environment.NewLine}Installer launched in a new window. Do not run any git or gvfs commands until the installer has completed.");
            consoleError = null;
            return true;
        }

        private bool TryCopyUpgradeTool(out string upgraderExePath, out string consoleError)
        {
            upgraderExePath = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCopyUpgradeTool), EventLevel.Informational))
            {
                if (!this.upgrader.TrySetupToolsDirectory(out upgraderExePath, out consoleError))
                {
                    return false;
                }

                activity.RelatedInfo($"Successfully Copied upgrade tool to {upgraderExePath}");
            }

            return true;
        }

        private bool TryLaunchUpgradeTool(string path, out string consoleError)
        {
            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryLaunchUpgradeTool), EventLevel.Informational))
            {
                Exception exception;
                string args = string.Empty + (this.DryRun ? $" {DryRunOption}" : string.Empty) + (this.NoVerify ? $" {NoVerifyOption}" : string.Empty);
                if (!this.processLauncher.TryStart(path, args, out exception))
                {
                    if (exception != null)
                    {
                        consoleError = exception.Message;
                        this.tracer.RelatedError($"Error launching upgrade tool. {exception.ToString()}");
                    }
                    else
                    {
                        consoleError = "Error launching upgrade tool";
                    }

                    return false;
                }

                activity.RelatedInfo("Successfully launched upgrade tool.");
            }

            consoleError = null;
            return true;
        }

        private bool TryCheckUpgradeAvailable(
            out Version latestVersion,
            out string error)
        {
            latestVersion = null;
            error = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckUpgradeAvailable), EventLevel.Informational))
            {
                bool checkSucceeded = false;
                Version version = null;

                checkSucceeded = this.upgrader.TryQueryNewestVersion(out version, out error);
                if (!checkSucceeded)
                {
                    return false;
                }

                latestVersion = version;

                activity.RelatedInfo($"Successfully checked server for GVFS upgrades. New version available {latestVersion}");
            }

            return true;
        }

        private bool TryCheckUpgradeInstallable(out string consoleError)
        {
            consoleError = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckUpgradeInstallable), EventLevel.Informational))
            {
                if (!this.prerunChecker.TryRunPreUpgradeChecks(
                    out consoleError))
                {
                    return false;
                }

                activity.RelatedInfo("Upgrade is installable.");
            }

            return true;
        }

        private bool ShouldRunUpgraderTool()
        {
            return this.Confirmed || this.DryRun;
        }

        private void ReportInfoToConsole(string message, params object[] args)
        {
            this.Output.WriteLine(message, args);
        }

        public class ProcessLauncher
        {
            public ProcessLauncher()
            {
                this.Process = new Process();
            }

            public Process Process { get; private set; }

            public virtual bool HasExited
            {
                get { return this.Process.HasExited; }
            }

            public virtual int ExitCode
            {
                get { return this.Process.ExitCode; }
            }

            public virtual bool TryStart(string path, string args, out Exception exception)
            {
                this.Process.StartInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    Arguments = args
                };

                exception = null;

                try
                {
                    return this.Process.Start();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                return false;
            }
        }
    }
}
