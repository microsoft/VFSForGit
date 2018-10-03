using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Upgrader
{
    public class InstallerPreRunChecker
    {
        private static readonly HashSet<string> BlockingProcessSet = new HashSet<string> { "GVFS", "GVFS.Mount", "git", "ssh-agent", "bash", "wish", "git-bash" };

        private ITracer tracer;

        public InstallerPreRunChecker(ITracer tracer, string commandToRerun)
        {
            this.tracer = tracer;
            this.CommandToRerun = commandToRerun;
        }

        protected string CommandToRerun { private get; set; }

        public bool TryRunPreUpgradeChecks(out string consoleError)
        {
            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryRunPreUpgradeChecks), EventLevel.Informational))
            {
                if (this.IsUnattended())
                {
                    consoleError = $"{GVFSConstants.UpgradeVerbMessages.GVFSUpgrade} is not supported in unattended mode";
                    this.tracer.RelatedError($"{nameof(TryRunPreUpgradeChecks)}: {consoleError}");
                    return false;
                }
                
                if (!this.IsGVFSUpgradeAllowed(out consoleError))
                {
                    this.tracer.RelatedError($"{nameof(TryRunPreUpgradeChecks)}: {consoleError}");
                    return false;
                }

                activity.RelatedInfo($"Successfully finished pre upgrade checks. Okay to run {GVFSConstants.UpgradeVerbMessages.GVFSUpgrade}.");
            }                

            consoleError = null;
            return true;
        }

        // TODO: Move repo mount calls to GVFS.Upgrader project.
        // https://github.com/Microsoft/VFSForGit/issues/293
        public bool TryMountAllGVFSRepos(out string consoleError)
        {
            return this.TryRunGVFSWithArgs("service --mount-all", out consoleError);
        }

        public bool TryUnmountAllGVFSRepos(out string consoleError)
        {
            consoleError = null;

            this.tracer.RelatedInfo("Unmounting any mounted GVFS repositories.");

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryUnmountAllGVFSRepos), EventLevel.Informational))
            {
                if (!this.TryRunGVFSWithArgs("service --unmount-all", out consoleError))
                {
                    this.tracer.RelatedError($"{nameof(TryUnmountAllGVFSRepos)}: {consoleError}");
                    return false;
                }

                // While checking for blocking processes like GVFS.Mount immediately after un-mounting, 
                // then sometimes GVFS.Mount shows up as running. But if the check is done after waiting 
                // for some time, then eventually GVFS.Mount goes away. The retry loop below is to help 
                // account for this delay between the time un-mount call returns and when GVFS.Mount
                // actually quits.
                this.tracer.RelatedInfo("Checking if GVFS or dependent processes are running.");
                int retryCount = 10;
                HashSet<string> processList = null;
                while (retryCount > 0)
                {
                    if (!this.IsBlockingProcessRunning(out processList))
                    {
                        break;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(250));
                    retryCount--;
                }

                if (processList.Count > 0)
                {
                    consoleError = string.Join(
                        Environment.NewLine,
                        "Blocking processes are running.",
                        $"Run {this.CommandToRerun} again after quitting these processes - " + string.Join(", ", processList.ToArray()));
                    this.tracer.RelatedError($"{nameof(TryUnmountAllGVFSRepos)}: {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully unmounted repositories.");
            }

            return true;
        }

        protected virtual bool IsElevated()
        {
            return GVFSPlatform.Instance.IsElevated();
        }

        protected virtual bool IsGVFSUpgradeSupported()
        {
            return GVFSPlatform.Instance.KernelDriver.IsGVFSUpgradeSupported();
        }

        protected virtual bool IsServiceInstalledAndNotRunning()
        {
            GVFSPlatform.Instance.IsServiceInstalledAndRunning(GVFSConstants.Service.ServiceName, out bool isInstalled, out bool isRunning);

            return isInstalled && !isRunning;
        }

        protected virtual bool IsUnattended()
        {
            return GVFSEnlistment.IsUnattended(this.tracer);
        }
        
        protected virtual bool IsBlockingProcessRunning(out HashSet<string> processes)
        {
            int currentProcessId = Process.GetCurrentProcess().Id;
            Process[] allProcesses = Process.GetProcesses();
            HashSet<string> matchingNames = new HashSet<string>();

            foreach (Process process in allProcesses)
            {
                if (process.Id == currentProcessId || !BlockingProcessSet.Contains(process.ProcessName))
                {
                    continue;
                }

                matchingNames.Add(process.ProcessName);
            }

            processes = matchingNames;
            return processes.Count > 0;
        }

        protected virtual bool TryRunGVFSWithArgs(string args, out string consoleError)
        {
            string gvfsDirectory = ProcessHelper.WhereDirectory(GVFSPlatform.Instance.Constants.GVFSExecutableName);
            if (!string.IsNullOrEmpty(gvfsDirectory))
            {
                string gvfsPath = Path.Combine(gvfsDirectory, GVFSPlatform.Instance.Constants.GVFSExecutableName);

                ProcessResult processResult = ProcessHelper.Run(gvfsPath, args);
                if (processResult.ExitCode == 0)
                {
                    consoleError = null;
                    return true;
                }
                else
                {
                    consoleError = string.IsNullOrEmpty(processResult.Errors) ? $"`gvfs {args}` failed." : processResult.Errors;
                    return false;
                }
            }
            else
            {
                consoleError = $"Could not locate {GVFSPlatform.Instance.Constants.GVFSExecutableName}";
                return false;
            }
        }

        private bool IsGVFSUpgradeAllowed(out string consoleError)
        {
            bool isConfirmed = string.Equals(this.CommandToRerun, GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm, StringComparison.OrdinalIgnoreCase);
            string adviceText = null;
            if (!this.IsElevated())
            {
                adviceText = isConfirmed ? $"Run {this.CommandToRerun} again from an elevated command prompt." : $"To install, run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} from an elevated command prompt.";
                consoleError = string.Join(
                    Environment.NewLine,
                    "The installer needs to be run from an elevated command prompt.",
                    adviceText);
                return false;
            }

            if (!this.IsGVFSUpgradeSupported())
            {
                consoleError = string.Join(
                    Environment.NewLine,
                    $"{GVFSConstants.UpgradeVerbMessages.GVFSUpgrade} is not supported because you have previously installed an out of band ProjFS driver.",
                    "Check your team's documentation for how to upgrade.");
                return false;
            }

            if (this.IsServiceInstalledAndNotRunning())
            {
                adviceText = isConfirmed ? $"Run `sc start GVFS.Service` and run {this.CommandToRerun} again from an elevated command prompt." : $"To install, run `sc start GVFS.Service` and run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} from an elevated command prompt.";
                consoleError = string.Join(
                    Environment.NewLine,
                    "GVFS Service is not running.",
                    adviceText);
                return false;
            }

            consoleError = null;
            return true;
        }
    }
}