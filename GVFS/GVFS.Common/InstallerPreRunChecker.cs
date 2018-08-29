using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.Upgrader
{
    public class InstallerPreRunChecker
    {
        private static readonly string[] BlockingProcessList = { "GVFS", "GVFS.Mount", "git", "ssh-agent", "bash", "gitk", "git-bash" };

        private ITracer tracer;

        public InstallerPreRunChecker(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public bool TryRunPreUpgradeChecks(GitVersion gitVersion, out string error)
        {
            this.tracer.RelatedInfo("Checking if GVFS upgrade can be run on this machine.");

            if (this.IsUnattended())
            {
                error = "Cannot run upgrade, when GVFS is running in unattended mode.";
                this.tracer.RelatedError($"{nameof(TryRunPreUpgradeChecks)}: {error}");
                return false;
            }

            if (this.IsDevelopmentVersion())
            {
                error = "Cannot run upgrade when development version of GVFS is installed.";
                this.tracer.RelatedError($"{nameof(TryRunPreUpgradeChecks)}: {error}");
                return false;
            }

            if (!this.IsGVFSUpgradeAllowed(out error))
            {
                this.tracer.RelatedError($"{nameof(TryRunPreUpgradeChecks)}: {error}");
                return false;
            }

            this.tracer.RelatedInfo("Okay to run GVFS upgrade.");
            this.tracer.RelatedInfo("Successfully finished pre upgrade checks.");

            error = null;
            return true;
        }

        public bool TryGetGitVersion(string gitPath, out GitVersion gitVersion, out string error)
        {
            error = null;
            gitVersion = null;

            string fakePath = string.Empty;
            GVFSEnlistment enlistment = new GVFSEnlistment(fakePath, fakePath, gitPath, fakePath);
            GitProcess.Result versionResult = GitProcess.Version(enlistment);
            string version = versionResult.Output;

            if (!GitVersion.TryParseGitVersionCommandResult(version, out gitVersion))
            {
                error = "Unable to determine installed git version. " + version;
                return false;
            }

            return true;
        }

        public virtual bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            return this.TryGetGitVersion(
                GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(), 
                out gitVersion, 
                out error);
        }

        public bool TryMountAllGVFSRepos(out string error)
        {
            return this.TryRunGVFSWithArgs("service --mount-all", out error);
        }

        public bool TryUnmountAllGVFSRepos(out string error)
        {
            error = null;

            this.tracer.RelatedInfo("Unmounting any mounted GVFS repositories.");

            if (!this.TryRunGVFSWithArgs("service --unmount-all", out error))
            {
                this.tracer.RelatedError($"{nameof(TryUnmountAllGVFSRepos)}: {error}");
                return false;
            }

            // While checking for blocking processes like GVFS.Mount immediately after un-mounting, 
            // then sometimes GVFS.Mount shows up as running. But if the check is done after waiting 
            // for some time, then eventually GVFS.Mount goes away. The retry loop below is to help 
            // account for this delay between the time un-mount call returns and when GVFS.Mount
            // actually quits.
            this.tracer.RelatedInfo("Checking if GVFS or dependent processes are running.");
            int retryCount = 10;
            List<string> processList = null;
            string thisProcessName = Process.GetCurrentProcess().ProcessName;
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
                error = "Please retry after quitting these processes - " + string.Join(", ", processList.ToArray());
                this.tracer.RelatedError($"{nameof(TryUnmountAllGVFSRepos)}: {error}");
                return false;
            }

            this.tracer.RelatedInfo("No GVFS or dependent processes are running.");
            this.tracer.RelatedInfo("Successfully unmounted repositories.");

            return true;
        }

        protected virtual bool IsElevated()
        {
            return GVFSPlatform.Instance.IsElevated();
        }

        protected virtual bool IsProjFSInboxed()
        {
            return GVFSPlatform.Instance.KernelDriver.IsGVFSUpgradeSupported();
        }

        protected virtual bool IsUnattended()
        {
            return GVFSEnlistment.IsUnattended(this.tracer);
        }

        protected virtual bool IsDevelopmentVersion()
        {
            return ProcessHelper.IsDevelopmentVersion();
        }

        protected virtual bool IsBlockingProcessRunning(out List<string> processes)
        {
            processes = new List<string>();

            bool isRunning = false;
            int currentProcessId = Process.GetCurrentProcess().Id;
            foreach (string processName in BlockingProcessList)
            {
                Process[] matches = Process.GetProcessesByName(processName);
                foreach (Process process in matches)
                {
                    if (process.Id == currentProcessId)
                    {
                        continue;
                    }
                    else
                    {
                        isRunning = true;
                        processes.Add(processName);
                        break;
                    }
                }
            }

            return isRunning;
        }

        protected virtual bool TryRunGVFSWithArgs(string args, out string error)
        {
            error = null;

            string gvfsPath = Path.Combine(
                ProcessHelper.WhereDirectory(GVFSPlatform.Instance.Constants.GVFSExecutableName),
                GVFSPlatform.Instance.Constants.GVFSExecutableName);

            ProcessResult processResult = ProcessHelper.Run(gvfsPath, args);
            if (processResult.ExitCode == 0)
            {
                return true;
            }
            else
            {
                string output = string.IsNullOrEmpty(processResult.Output) ? string.Empty : processResult.Output;
                string errorString = string.IsNullOrEmpty(processResult.Errors) ? "GVFS error" : processResult.Errors;
                error = string.Format("{0}. {1}", errorString, output);
                return false;
            }
        }

        private bool IsGVFSUpgradeAllowed(out string error)
        {
            error = null;

            if (!this.IsElevated())
            {
                error = "The installer needs to be run from an elevated command prompt.\nPlease open an elevated (administrator) command prompt and run gvfs upgrade again.";
                return false;
            }

            if (!this.IsProjFSInboxed())
            {
                error = "Unsupported ProjFS configuration.\nCheck your team's documentation for how to upgrade.";
                return false;
            }

            return true;
        }
    }
}