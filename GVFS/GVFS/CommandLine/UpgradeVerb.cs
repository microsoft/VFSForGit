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
            string errorOutputFormat = Environment.NewLine + "ERROR: {0}";
            string error = null;
            Version newestVersion = null;
            bool isInstallable = false;

            if (this.upgrader.IsNoneRing())
            {
                this.tracer.RelatedInfo($"{nameof(this.TryRunProductUpgrade)}: {GVFSConstants.UpgradeVerbMessages.NoneRingConsoleAlert}");
                this.ReportInfoToConsole(GVFSConstants.UpgradeVerbMessages.NoneRingConsoleAlert);
                this.ReportInfoToConsole(GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand);
                return true;
            }

            if (!this.TryRunUpgradeChecks(out newestVersion, out isInstallable, out error))
            {
                this.Output.WriteLine(errorOutputFormat, error);
                this.tracer.RelatedError($"{nameof(this.TryRunProductUpgrade)}: Upgrade checks failed. {error}");
                return false;
            }

            if (newestVersion == null)
            {
                this.ReportInfoToConsole($"Great news, you're all caught up on upgrades in the {this.upgrader.Ring} ring!");
                return true;
            }

            this.ReportInfoToConsole("New GVFS version available: {0}", newestVersion.ToString());
                        
            if (!this.Confirmed && isInstallable)
            {
                this.ReportInfoToConsole("Run `gvfs upgrade --confirm` to install it");
                return true;
            }

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

            return true;
        }

        private bool TryRunUpgradeChecks(
            out Version latestVersion,
            out bool isUpgradeInstallable,
            out string consoleError)
        {
            bool upgradeCheckSuccess = false;
            bool upgradeIsInstallable = false;
            string errorMessage = null;
            Version version = null;

            this.ShowStatusWhileRunning(
                () =>
                {
                    upgradeCheckSuccess = this.TryCheckUpgradeAvailable(out version, out errorMessage);
                    if (upgradeCheckSuccess && version != null)
                    {
                        upgradeIsInstallable = true;

                        if (!this.TryCheckUpgradeInstallable(out errorMessage))
                        {
                            upgradeIsInstallable = false;
                        }
                    }

                    return upgradeCheckSuccess;
                },
                 "Checking for GVFS upgrades",
                suppressGvfsLogMessage: true);

            latestVersion = version;
            isUpgradeInstallable = upgradeIsInstallable;
            consoleError = errorMessage;

            return upgradeCheckSuccess;
        }

        private bool TryRunInstaller(out string consoleError)
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
                consoleError = errorMessage;
                return false;
            }

            consoleError = null;
            return true;
        }

        private bool TryCopyUpgradeTool(out string upgraderExePath, out string consoleError)
        {
            upgraderExePath = null;

            this.tracer.RelatedInfo("Copying upgrade tool");

            if (!this.upgrader.TrySetupToolsDirectory(out upgraderExePath, out consoleError))
            {
                return false;
            }

            this.tracer.RelatedInfo($"Successfully Copied upgrade tool to {upgraderExePath}");

            return true;
        }

        private bool TryLaunchUpgradeTool(string path, out string consoleError)
        {
            this.tracer.RelatedInfo("Launching upgrade tool");

            Exception exception;
            if (!this.processWrapper.TryStart(path, out exception))
            {
                if (exception != null)
                {
                    consoleError = exception.Message;
                    this.tracer.RelatedError($"Error launching upgrade tool. {exception.ToString()}");
                }
                else
                {
                    consoleError = $"Error launching upgrade tool";
                }
                
                return false;
            }
            
            this.tracer.RelatedInfo("Successfully launched upgrade tool.");

            consoleError = null;
            return true;
        }

        private bool TryCheckUpgradeAvailable(
            out Version latestVersion, 
            out string consoleError)
        {
            latestVersion = null;
            consoleError = null;

            this.tracer.RelatedInfo("Checking server for available upgrades.");

            bool checkSucceeded = false;
            Version version = null;

            checkSucceeded = this.upgrader.TryGetNewerVersion(out version, out consoleError);
            if (!checkSucceeded)
            {
                return false;
            }

            latestVersion = version;

            this.tracer.RelatedInfo("Successfully checked server for GVFS upgrades.");

            return true;
        }

        private bool TryCheckUpgradeInstallable(out string consoleError)
        {
            consoleError = null;

            this.tracer.RelatedInfo("Checking if upgrade is installable on this machine.");

            this.prerunChecker.CommandToRerun = this.Confirmed ? "gvfs upgrade --confirm" : "gvfs upgrade";

            if (!this.prerunChecker.TryRunPreUpgradeChecks(
                out consoleError))
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