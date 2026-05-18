using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Service
{
    /// <summary>
    /// Monitors GVFS.Mount process exits and applies staged upgrades when
    /// all mount processes have exited. Event-driven — no polling.
    ///
    /// The installer always replaces GVFS.Service.exe in-place, so this
    /// monitor runs as part of the new service version regardless of what
    /// version of gvfs.exe the user has. This solves the bootstrap problem
    /// where old gvfs.exe clients cannot send new pipe messages.
    ///
    /// Lock ordering: syncLock is never held when calling into
    /// PendingUpgradeHandler (which acquires its own ApplyLock).
    /// TryApplyPendingUpgrade is called outside syncLock, then syncLock
    /// is re-acquired to act on the result.
    /// </summary>
    public sealed class PendingUpgradeMonitor : IDisposable
    {
        private const int DebouncePeriodMs = 1000;

        private readonly ITracer tracer;
        private readonly Lock syncLock = new Lock();
        private List<Process> trackedProcesses = new List<Process>();
        private Timer debounceTimer;
        private bool disposed;

        public PendingUpgradeMonitor(ITracer tracer)
        {
            this.tracer = tracer;
        }

        /// <summary>
        /// Begin monitoring GVFS.Mount processes. When all exit, attempts
        /// to apply the pending upgrade. If new mounts start in the
        /// meantime, re-registers on those.
        /// </summary>
        public void Start()
        {
            this.tracer.RelatedInfo($"{nameof(PendingUpgradeMonitor)}: Starting mount process monitor for pending upgrade");
            this.RegisterOnMountProcesses();
        }

        public void Dispose()
        {
            lock (this.syncLock)
            {
                if (this.disposed)
                {
                    return;
                }

                this.disposed = true;
                this.CleanupTrackedProcesses();

                if (this.debounceTimer != null)
                {
                    this.debounceTimer.Dispose();
                    this.debounceTimer = null;
                }
            }
        }

        private void RegisterOnMountProcesses()
        {
            lock (this.syncLock)
            {
                if (this.disposed)
                {
                    return;
                }

                this.CleanupTrackedProcesses();

                List<Process> mountProcesses;
                try
                {
                    mountProcesses = PendingUpgradeHandler.GetInstalledMountProcesses(this.tracer);
                }
                catch (Exception ex)
                {
                    this.tracer.RelatedWarning(
                        $"{nameof(PendingUpgradeMonitor)}: Failed to enumerate mount processes: {ex.Message}");
                    return;
                }

                if (mountProcesses.Count == 0)
                {
                    this.tracer.RelatedInfo(
                        $"{nameof(PendingUpgradeMonitor)}: No mount processes found, scheduling upgrade check");
                    this.ScheduleDebouncedCheck();
                    return;
                }

                this.tracer.RelatedInfo(
                    $"{nameof(PendingUpgradeMonitor)}: Monitoring {mountProcesses.Count} mount process(es) for exit");

                bool anyAlive = false;
                foreach (Process process in mountProcesses)
                {
                    bool added = false;
                    try
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += this.OnMountProcessExited;
                        this.trackedProcesses.Add(process);
                        added = true;

                        if (process.HasExited)
                        {
                            this.ScheduleDebouncedCheck();
                        }
                        else
                        {
                            anyAlive = true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        if (!added)
                        {
                            process.Dispose();
                        }

                        this.ScheduleDebouncedCheck();
                    }
                    catch (Win32Exception ex)
                    {
                        this.tracer.RelatedWarning(
                            $"{nameof(PendingUpgradeMonitor)}: Cannot monitor PID {process.Id}: {ex.Message}");
                        if (!added)
                        {
                            process.Dispose();
                        }
                    }
                }

                if (!anyAlive)
                {
                    this.ScheduleDebouncedCheck();
                }
            }
        }

        private void OnMountProcessExited(object sender, EventArgs e)
        {
            Process exitedProcess = sender as Process;
            int pid = 0;
            try
            {
                pid = exitedProcess?.Id ?? 0;
            }
            catch (InvalidOperationException)
            {
            }

            this.tracer.RelatedInfo(
                $"{nameof(PendingUpgradeMonitor)}: Mount process exited (PID {pid})");

            lock (this.syncLock)
            {
                this.ScheduleDebouncedCheck();
            }
        }

        /// <summary>
        /// Must be called while holding syncLock.
        /// </summary>
        private void ScheduleDebouncedCheck()
        {
            if (this.disposed)
            {
                return;
            }

            if (this.debounceTimer == null)
            {
                this.debounceTimer = new Timer(
                    this.OnDebounceTimerFired,
                    null,
                    DebouncePeriodMs,
                    Timeout.Infinite);
            }
            else
            {
                this.debounceTimer.Change(DebouncePeriodMs, Timeout.Infinite);
            }
        }

        private void OnDebounceTimerFired(object state)
        {
            lock (this.syncLock)
            {
                if (this.disposed)
                {
                    return;
                }
            }

            this.tracer.RelatedInfo(
                $"{nameof(PendingUpgradeMonitor)}: Checking pending upgrade after mount process exit");

            UpgradeResult result = PendingUpgradeHandler.TryApplyPendingUpgrade(this.tracer);

            lock (this.syncLock)
            {
                if (this.disposed)
                {
                    return;
                }

                switch (result)
                {
                    case UpgradeResult.DeferredMountsRunning:
                        this.tracer.RelatedInfo(
                            $"{nameof(PendingUpgradeMonitor)}: New mounts detected, re-registering");
                        this.RegisterOnMountProcesses();
                        break;

                    case UpgradeResult.Applied:
                        this.tracer.RelatedInfo(
                            $"{nameof(PendingUpgradeMonitor)}: Upgrade applied successfully, stopping monitor");
                        this.CleanupTrackedProcesses();
                        break;

                    case UpgradeResult.NoPending:
                        this.tracer.RelatedInfo(
                            $"{nameof(PendingUpgradeMonitor)}: No pending upgrade, stopping monitor");
                        this.CleanupTrackedProcesses();
                        break;

                    default:
                        this.tracer.RelatedWarning(
                            $"{nameof(PendingUpgradeMonitor)}: Upgrade returned {result}, stopping monitor");
                        this.CleanupTrackedProcesses();
                        break;
                }
            }
        }

        private void CleanupTrackedProcesses()
        {
            foreach (Process process in this.trackedProcesses)
            {
                try
                {
                    process.Exited -= this.OnMountProcessExited;
                }
                catch (Exception)
                {
                }

                process.Dispose();
            }

            this.trackedProcesses.Clear();
        }
    }
}
