using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

namespace RGFS.Common
{
    /// <summary>
    /// Watch for process termination events given a specific PID.
    /// </summary>
    internal class ProcessWatcher : IDisposable
    {
        private const string CommandParentExePrefix = "git";
        private const string RGFSExe = "rgfs.exe";

        private const string QueryTemplate = @"SELECT * FROM __InstanceDeletionEvent  WITHIN 1 WHERE TargetInstance ISA 'Win32_Process' and TargetInstance.ProcessId = '{0}'";

        private readonly object terminationLock = new object();
        private readonly Action<int> onProcessTerminated;
        private ManagementEventWatcher watcher;

        private int? currentPid;
        private int? pendingPid;

        public ProcessWatcher(Action<int> onProcessTerminated)
        {
            this.onProcessTerminated = onProcessTerminated;
            this.watcher = new ManagementEventWatcher();
            this.watcher.EventArrived += this.EventArrived;
        }

        public void WatchForTermination(int pid)
        {
            lock (this.terminationLock)
            {
                this.pendingPid = pid;
                this.currentPid = null;
                this.watcher.Query = new EventQuery("WQL", string.Format(QueryTemplate, this.pendingPid));
            }

            // Start the watch async since it takes > 100ms and there's no need to make the client wait.
            Task.Run(() =>
            {
                lock (this.terminationLock)
                {
                    if (this.pendingPid != null)
                    {
                        Process process;
                        if (ProcessHelper.TryGetProcess(this.pendingPid.Value, out process) && this.ShouldWatchProcess(process))
                        {
                            this.watcher.Start();
                            this.currentPid = this.pendingPid;
                        }
                        else
                        {
                            this.onProcessTerminated(this.pendingPid.Value);
                        }

                        this.pendingPid = null;
                    }
                }
            });
        }

        public void StopWatching(int pid)
        {
            Task.Run(() =>
            {
                lock (this.terminationLock)
                {
                    if (this.pendingPid != null)
                    {
                        if (this.pendingPid.Value == pid)
                        {
                            this.pendingPid = null;
                        }

                        return;
                    }
                    else if (pid == this.currentPid)
                    {
                        this.watcher.Stop();
                        this.currentPid = null;
                    }
                }
            });
        }

        public void Dispose()
        {
            if (this.watcher != null)
            {
                if (this.currentPid != null)
                {
                    this.watcher.Stop();
                    this.currentPid = null;
                }

                this.watcher.Dispose();
                this.watcher = null;
            }
        }

        private void EventArrived(object sender, EventArrivedEventArgs e)
        {
            lock (this.terminationLock)
            {
                if (this.currentPid == null)
                {
                    // Not expecting a termination notification since the post-command already released the lock.
                    return;
                }

                ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;

                uint eventPid = (uint)targetInstance.Properties["ProcessId"].Value;
                if (eventPid == this.currentPid)
                {
                    this.watcher.Stop();
                    this.onProcessTerminated(this.currentPid.Value);
                    this.currentPid = null;
                }
            }
        }

        private bool ShouldWatchProcess(Process process)
        {
            return
                (process.MainModule.ModuleName.StartsWith(CommandParentExePrefix, StringComparison.OrdinalIgnoreCase) &&
                 process.MainModule.ModuleName.EndsWith(RGFSConstants.ExecutableExtension, StringComparison.OrdinalIgnoreCase)) ||
                 process.MainModule.ModuleName.Equals(RGFSExe, StringComparison.OrdinalIgnoreCase);
        }
    }
}
