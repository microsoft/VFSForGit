using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(UpgradeVerbName, HelpText = "Checks if a new GVFS release is available.")]
    public class UpgradeVerb : GVFSVerb
    {
        private const string UpgradeVerbName = "upgrade";
        private ITracer tracer;
        private ProductUpgrader upgrader;
        private InstallerPreRunChecker prerunChecker;
        private ProcessLauncher processWrapper;

        public UpgradeVerb(
            ProductUpgrader upgrader,
            ITracer tracer,
            InstallerPreRunChecker prerunChecker,
            ProcessLauncher processWrapper,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.prerunChecker = prerunChecker;
            this.processWrapper = processWrapper;
            this.Output = output;
        }

        public UpgradeVerb()
        {
            JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeVerb");
            string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                ProductUpgrader.GetLogDirectoryPath(),
                GVFSConstants.LogFileTypes.UpgradeVerb);
            jsonTracer.AddLogFileEventListener(logFilePath, EventLevel.Informational, Keywords.Any);

            this.tracer = jsonTracer;
            this.prerunChecker = new InstallerPreRunChecker(this.tracer);
            this.processWrapper = new ProcessLauncher();
            this.Output = Console.Out;
            this.upgrader = new ProductUpgrader(ProcessHelper.GetCurrentProcessVersion(), this.tracer);
        }

        [Option(
            "confirm",
            Default = false,
            Required = false,
            HelpText = "Pass in this flag to actually install the newest release")]
        public bool Confirmed { get; set; }

        public override string EnlistmentRootPathParameter { get; set; }

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }
        
        public override void Execute()
        {
            ReturnCode exitCode = ReturnCode.Success;
            if (!this.TryRunProductUpgrade())
            {
                exitCode = ReturnCode.GenericError;
                this.ReportErrorAndExit(this.tracer, exitCode, string.Empty);
            }
        }
        
        private bool TryRunProductUpgrade()
        {
            string error = null;
            Version newestVersion = null;
            bool isInstallable = false;

            if (this.upgrader.IsNoneRing())
            {
                string message = "Upgrade ring set to None. No upgrade check was performed.";
                this.tracer.RelatedInfo($"{nameof(this.TryRunProductUpgrade)}: {message}");
                this.ReportInfoToConsole(message);
                return true;
            }

            if (!this.TryRunUpgradeChecks(out newestVersion, out isInstallable, out error))
            {
                this.Output.WriteLine($"{error}");
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade checks failed. {error}");
                return false;
            }

            if (newestVersion == null)
            {
                this.ReportInfoToConsole($"Great news, you're all caught up on upgrades in the {this.upgrader.Ring} ring!");
                return true;
            }

            this.ReportInfoToConsole("New GVFS version available: {0}", newestVersion.ToString());

            if (!isInstallable)
            {
                string message = "Upgrade is not installable." + Environment.NewLine + error;
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: {error}");
                this.ReportInfoToConsole(message);
                return false;
            }

            if (!this.Confirmed)
            {
                this.ReportInfoToConsole("Run gvfs upgrade --confirm to install it");
                return true;
            }

            if (!this.TryRunInstaller(out error))
            {
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Could not launch installer. {error}");
                this.ReportInfoToConsole($"Could not launch installer. {error}");
                return false;
            }

            return true;
        }

        private bool TryRunUpgradeChecks(
            out Version latestVersion,
            out bool installable,
            out string error)
        {
            bool upgradeCheckSuccess = false;
            bool upgradeInstallable = false;
            string errorMessage = null;
            Version version = null;

            this.ShowStatusWhileRunning(
                () =>
                {
                    upgradeCheckSuccess = this.TryCheckUpgradeAvailable(out version, out errorMessage);
                    if (upgradeCheckSuccess && version != null)
                    {
                        upgradeInstallable = true;

                        if (!this.TryCheckUpgradeInstallable(out errorMessage))
                        {
                            upgradeInstallable = false;
                        }
                    }

                    return upgradeCheckSuccess;
                },
                 "Checking for GVFS upgrades",
                suppressGvfsLogMessage: true);

            latestVersion = version;
            installable = upgradeInstallable;
            error = errorMessage;

            return upgradeCheckSuccess;
        }

        private bool TryRunInstaller(out string error)
        {
            string upgraderPath = null;
            string errorMessage = null;

            bool preUpgradeSuccess = this.ShowStatusWhileRunning(
                () =>
                {
                    if (this.TryCopyUpgradeTool(out upgraderPath, out errorMessage) &&
                        this.TryLaunchUpgradeTool(upgraderPath, out errorMessage))
                    {
                        return true;
                    }

                    return false;
                },
                "Launching upgrade tool",
                suppressGvfsLogMessage: true);
            
            if (!preUpgradeSuccess)
            {
                error = errorMessage;
                return false;
            }

            error = null;
            return true;
        }

        private bool TryCopyUpgradeTool(out string upgraderExePath, out string error)
        {
            upgraderExePath = null;

            this.tracer.RelatedInfo("Copying upgrade tool");

            if (!this.upgrader.TryCreateToolsDirectory(out upgraderExePath, out error))
            {
                return false;
            }

            this.tracer.RelatedInfo("Successfully Copied upgrade tool.");

            return true;
        }

        private bool TryLaunchUpgradeTool(string path, out string error)
        {
            this.tracer.RelatedInfo("Launching upgrade tool");

            if (!this.processWrapper.Start(path))
            {
                error = "Error launching upgrade tool";
                return false;
            }
            
            this.tracer.RelatedInfo("Successfully launched upgrade tool.");

            error = null;
            return true;
        }

        private bool TryCheckUpgradeAvailable(
            out Version latestVersion, 
            out string error)
        {
            latestVersion = null;
            error = null;

            this.tracer.RelatedInfo("Checking server for available upgrades.");

            bool checkSucceeded = false;
            Version version = null;

            checkSucceeded = this.upgrader.TryGetNewerVersion(out version, out error);
            if (!checkSucceeded)
            {
                return false;
            }

            latestVersion = version;

            this.tracer.RelatedInfo("Successfully checked server for GVFS upgrades.");

            return true;
        }

        private bool TryCheckUpgradeInstallable(out string error)
        {
            error = null;

            this.tracer.RelatedInfo("Checking if upgrade is installable on this machine.");

            GitVersion gitVersion = null;
            if (!this.upgrader.TryGetGitVersion(out gitVersion, out error))
            {
                return false;
            }

            if (!this.prerunChecker.TryRunPreUpgradeChecks(
                gitVersion,
                out error))
            {
                return false;
            }

            this.tracer.RelatedInfo("Upgrade is installable.");

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

            public virtual bool Start(string path)
            {
                this.Process.StartInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                return this.Process.Start();
            }
        }
    }
}