using CommandLine;
using GVFS.Common;
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
        private ITracer tracer;
        private ProductUpgraderBase upgrader;
        private InstallerPreRunChecker prerunChecker;
        private ProcessLauncher processLauncher;

        public UpgradeVerb(
            ProductUpgraderBase upgrader,
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

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }

        public override void Execute()
        {
            ReturnCode exitCode = ReturnCode.Success;
            if (!this.TryInitializeUpgrader() || !this.TryRunProductUpgrade())
            {
                exitCode = ReturnCode.GenericError;
                this.ReportErrorAndExit(this.tracer, exitCode, string.Empty);
            }
        }

        private bool TryInitializeUpgrader()
        {
            if (GVFSPlatform.Instance.SupportsGVFSUpgrade)
            {
                if (this.upgrader == null)
                {
                    JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeVerb");
                    string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                        ProductUpgrader.GetLogDirectoryPath(),
                        GVFSConstants.LogFileTypes.UpgradeVerb);
                    jsonTracer.AddLogFileEventListener(logFilePath, EventLevel.Informational, Keywords.Any);

                    this.tracer = jsonTracer;
                    this.prerunChecker = new InstallerPreRunChecker(this.tracer, this.Confirmed ? GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm : GVFSConstants.UpgradeVerbMessages.GVFSUpgrade);

                    string errorMessage;
                    this.upgrader = ProductUpgraderBase.LoadUpgrader(GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(), this.tracer, out errorMessage);
                }

                return true;
            }
            else
            {
                this.ReportInfoToConsole($"ERROR: {GVFSConstants.UpgradeVerbMessages.GVFSUpgrade} is not supported on this operating system.");
                return false;
            }
        }

        private bool TryRunProductUpgrade()
        {
            string errorOutputFormat = Environment.NewLine + "ERROR: {0}";
            string error = null;
            string cannotInstallReason = null;
            Version newestVersion = null;

            bool isInstallable = this.TryCheckUpgradeInstallable(out cannotInstallReason);
            if (this.Confirmed && !isInstallable)
            {
                this.ReportInfoToConsole($"Cannot upgrade GVFS on this machine.");
                this.Output.WriteLine(errorOutputFormat, cannotInstallReason);
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade is not installable. {cannotInstallReason}");
                return false;
            }

            if (!this.TryRunUpgradeChecks(out newestVersion, out error))
            {
                this.Output.WriteLine(errorOutputFormat, error);
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade checks failed. {error}");
                return false;
            }

            if (newestVersion == null)
            {
                this.ReportInfoToConsole($"Great news, you're all caught up on upgrades!");
                return true;
            }

            string upgradeAvailableMessage = $"New GVFS version {newestVersion.ToString()} available.";
            if (this.Confirmed)
            {
                this.ReportInfoToConsole(upgradeAvailableMessage);

                if (!isInstallable)
                {
                    this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: {error}");
                    this.Output.WriteLine(errorOutputFormat, error);
                    return false;
                }

                if (!this.TryRunInstaller(out error))
                {
                    this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Could not launch upgrade tool. {error}");
                    this.Output.WriteLine(errorOutputFormat, "Could not launch upgrade tool. " + error);
                    return false;
                }
            }
            else
            {
                string message = string.Join(
                        Environment.NewLine,
                        GVFSConstants.UpgradeVerbMessages.UnmountRepoWarning,
                        GVFSConstants.UpgradeVerbMessages.UpgradeInstallAdvice);
                this.ReportInfoToConsole(upgradeAvailableMessage + Environment.NewLine + Environment.NewLine + message + Environment.NewLine);
            }

            return true;
        }

        private bool TryRunUpgradeChecks(
            out Version latestVersion,
            out string consoleError)
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
            consoleError = errorMessage;

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
                if (!this.processLauncher.TryStart(path, out exception))
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
            out string consoleError)
        {
            latestVersion = null;
            consoleError = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckUpgradeAvailable), EventLevel.Informational))
            {
                bool checkSucceeded = false;
                Version version = null;

                checkSucceeded = this.upgrader.TryGetNewerVersion(out version, out consoleError);
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

            public virtual bool TryStart(string path, out Exception exception)
            {
                this.Process.StartInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
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