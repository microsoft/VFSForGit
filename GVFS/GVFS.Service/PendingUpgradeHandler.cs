using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.Service
{
    /// <summary>
    /// Detects and applies staged upgrades from the PendingUpgrade directory.
    ///
    /// When the installer runs with mounts active, it stages new files to
    /// {installDir}\PendingUpgrade\ instead of replacing files in-place.
    /// This class applies the upgrade when no GVFS.Mount processes are
    /// running — either on service start (before automount), after a
    /// repo unmount (via deferred check from RequestHandler), or when
    /// PendingUpgradeMonitor detects all mount processes have exited.
    ///
    ///   1. Move old files from install dir → PreviousVersion\
    ///   2. Move new files from PendingUpgrade\ → install dir
    ///   3. Delete PreviousVersion\ and PendingUpgrade\
    ///
    /// File.Move on the same volume is an atomic rename at the filesystem
    /// level, so a crash mid-upgrade leaves files intact (either at the old
    /// or new location). On retry, the handler resumes from where it left off.
    ///
    /// With native AOT, each exe is self-contained — the only locked file
    /// is GVFS.Service.exe itself, which the installer already replaced.
    /// </summary>
    public static class PendingUpgradeHandler
    {
        public const string PendingUpgradeDirectoryName = "PendingUpgrade";
        private const string PreviousVersionDirectoryName = "PreviousVersion";
        private const string ReadyMarkerFileName = ".ready";
        private const string Phase1CompleteMarkerFileName = ".phase1-complete";
        private const string ServiceExeName = "GVFS.Service.exe";
        private const string MountProcessName = "GVFS.Mount";
        private const string MountExeName = "GVFS.Mount.exe";

        private static readonly Lock ApplyLock = new Lock();

        // Executables that users or the service can launch to start new
        // mount/hook processes. During upgrade these are moved out first
        // (Phase 1) and moved in last (Phase 2) so that no new GVFS
        // processes can start while the upgrade is in progress.
        // Ordered most-likely-to-be-called first for Phase 1 removal.
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly string[] PriorityExes = new[]
        {
            "GVFS.Hooks.exe",
            "GVFS.exe",
            "GVFS.Mount.exe",
        };

        /// <summary>
        /// Checks for and applies a pending staged upgrade.
        /// </summary>
        public static UpgradeResult TryApplyPendingUpgrade(ITracer tracer)
        {
            lock (ApplyLock)
            {
                return TryApplyPendingUpgradeLocked(tracer);
            }
        }

        /// <summary>
        /// Returns true if a PendingUpgrade directory with a .ready marker exists.
        /// </summary>
        public static bool IsPending()
        {
            string pendingUpgradeDir = Path.Combine(Configuration.AssemblyPath, PendingUpgradeDirectoryName);
            if (!Directory.Exists(pendingUpgradeDir))
            {
                return false;
            }

            string readyMarker = Path.Combine(pendingUpgradeDir, ReadyMarkerFileName);
            return File.Exists(readyMarker);
        }

        /// <summary>
        /// Returns GVFS.Mount processes whose executable is in the install
        /// directory (or any versioned subdirectory). Processes from dev builds
        /// or other installs are excluded so they don't block upgrades of the
        /// system install. If a process's path cannot be read (access denied,
        /// 32/64-bit mismatch), it is included conservatively.
        /// Caller must dispose the returned Process objects.
        /// </summary>
        public static List<Process> GetInstalledMountProcesses(ITracer tracer)
        {
            string installDir = Configuration.AssemblyPath;
            Process[] allMountProcesses = Process.GetProcessesByName(MountProcessName);
            List<Process> installed = new List<Process>();

            foreach (Process process in allMountProcesses)
            {
                bool include = true;
                try
                {
                    string processPath = process.MainModule?.FileName;
                    if (processPath != null && !IsInstalledMountPath(installDir, processPath))
                    {
                        include = false;
                        tracer.RelatedInfo(
                            $"{nameof(PendingUpgradeHandler)}: Skipping GVFS.Mount PID {process.Id} " +
                            $"(path: {processPath}, not in install dir)");
                    }
                }
                catch (Exception)
                {
                    // Access denied or process exited — include conservatively
                }

                if (include)
                {
                    installed.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }

            return installed;
        }

        private static UpgradeResult TryApplyPendingUpgradeLocked(ITracer tracer)
        {
            string installDir = Configuration.AssemblyPath;
            string pendingUpgradeDir = Path.Combine(installDir, PendingUpgradeDirectoryName);
            string previousVersionDir = Path.Combine(installDir, PreviousVersionDirectoryName);

            if (!Directory.Exists(pendingUpgradeDir))
            {
                TryDeleteDirectory(tracer, previousVersionDir, "leftover PreviousVersion");
                return UpgradeResult.NoPending;
            }

            string readyMarker = Path.Combine(pendingUpgradeDir, ReadyMarkerFileName);
            if (!File.Exists(readyMarker))
            {
                EventMetadata readyMetadata = new EventMetadata();
                readyMetadata.Add("PendingUpgradeDir", pendingUpgradeDir);
                tracer.RelatedWarning(
                    readyMetadata,
                    $"{nameof(PendingUpgradeHandler)}: PendingUpgrade directory exists but {ReadyMarkerFileName} marker " +
                    "is missing — installer was likely interrupted. Skipping until next install completes.",
                    Keywords.Telemetry);
                return UpgradeResult.NotReady;
            }

            tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Pending upgrade detected at {pendingUpgradeDir}");

            List<Process> mountProcesses = new List<Process>();
            try
            {
                mountProcesses = GetInstalledMountProcesses(tracer);
                if (mountProcesses.Count > 0)
                {
                    EventMetadata deferMetadata = new EventMetadata();
                    deferMetadata.Add("MountProcessCount", mountProcesses.Count);
                    tracer.RelatedEvent(
                        EventLevel.Informational,
                        $"{nameof(PendingUpgradeHandler)}_Deferred",
                        deferMetadata,
                        Keywords.Telemetry);
                    return UpgradeResult.DeferredMountsRunning;
                }
            }
            finally
            {
                foreach (Process p in mountProcesses)
                {
                    p.Dispose();
                }
            }

            try
            {
                // Phase 1: Move old files to PreviousVersion (backup for rollback).
                // priority exes (GVFS.exe, GVFS.Hooks.exe, GVFS.Mount.exe) are
                // moved FIRST so no new GVFS processes can start during the upgrade.
                // Use a marker file to track completion — directory existence alone
                // is insufficient because a crash mid-phase leaves the directory
                // with only some files backed up.
                string[] stagedFiles = Directory.GetFiles(pendingUpgradeDir, "*", SearchOption.AllDirectories);
                string phase1Marker = Path.Combine(previousVersionDir, Phase1CompleteMarkerFileName);
                if (!File.Exists(phase1Marker))
                {
                    // Clean up any partial Phase 1 from a prior crash — re-run
                    // from scratch to ensure all files are backed up.
                    if (Directory.Exists(previousVersionDir))
                    {
                        tracer.RelatedWarning(
                            $"{nameof(PendingUpgradeHandler)}: Phase 1 incomplete from prior attempt, restarting backup",
                            Keywords.Telemetry);
                        TryRestoreFromPreviousVersion(tracer, previousVersionDir, installDir);
                        TryDeleteDirectory(tracer, previousVersionDir, "incomplete PreviousVersion");
                    }

                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 - backing up {stagedFiles.Length} file(s) to PreviousVersion");

                    int backedUp = 0;
                    foreach (string relativePath in OrderForRemoval(stagedFiles, pendingUpgradeDir))
                    {
                        string installedFile = Path.Combine(installDir, relativePath);
                        if (File.Exists(installedFile))
                        {
                            string backupFile = Path.Combine(previousVersionDir, relativePath);
                            string backupDir = Path.GetDirectoryName(backupFile);
                            if (!Directory.Exists(backupDir))
                            {
                                Directory.CreateDirectory(backupDir);
                            }

                            MoveFileWithRetry(tracer, installedFile, backupFile);
                            backedUp++;
                        }
                    }

                    File.WriteAllText(phase1Marker, string.Empty);
                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 complete. Backed up {backedUp} file(s)");
                }
                else
                {
                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 already done ({Phase1CompleteMarkerFileName} exists). Resuming phase 2.");
                }

                // Phase 2: Move new files from PendingUpgrade to install dir.
                // priority exes are moved LAST so all supporting files (DLLs,
                // hooks, etc.) are in place before any GVFS process can start.
                tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 2 - applying {stagedFiles.Length} staged file(s)");

                int applied = 0;
                foreach (string relativePath in OrderForInstall(stagedFiles, pendingUpgradeDir))
                {
                    string sourceFile = Path.Combine(pendingUpgradeDir, relativePath);
                    string destFile = Path.Combine(installDir, relativePath);
                    string destDir = Path.GetDirectoryName(destFile);
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // If dest already exists (phase 2 partially completed on a prior
                    // run), delete it first so File.Move can succeed.
                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }

                    MoveFileWithRetry(tracer, sourceFile, destFile);
                    applied++;
                }

                tracer.RelatedInfo(
                    $"{nameof(PendingUpgradeHandler)}: Phase 2 complete. Applied={applied}");

                // Phase 3: Clean up
                // Capture old version before deleting PreviousVersion.
                string oldVersion = TryGetOldVersion(previousVersionDir);

                // Delete the skipped GVFS.Service.exe from PendingUpgrade first,
                // otherwise Directory.Delete will fail on the non-empty directory.
                string skippedServiceExe = Path.Combine(pendingUpgradeDir, ServiceExeName);
                if (File.Exists(skippedServiceExe))
                {
                    File.Delete(skippedServiceExe);
                }

                TryDeleteDirectory(tracer, pendingUpgradeDir, "PendingUpgrade");
                TryDeleteDirectory(tracer, previousVersionDir, "PreviousVersion");

                string newVersion = ProcessHelper.GetCurrentProcessVersion();
                EventMetadata successMetadata = new EventMetadata();
                successMetadata.Add("NewVersion", newVersion);
                successMetadata.Add("OldVersion", oldVersion ?? "unknown");
                successMetadata.Add("FilesApplied", applied);
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    $"{nameof(PendingUpgradeHandler)}_Complete",
                    successMetadata,
                    Keywords.Telemetry);
                return UpgradeResult.Applied;
            }
            catch (Exception ex)
            {
                EventMetadata errorMetadata = new EventMetadata();
                errorMetadata.Add("Exception", ex.ToString());
                tracer.RelatedError(
                    errorMetadata,
                    $"{nameof(PendingUpgradeHandler)}: Upgrade failed: {ex.Message}. " +
                    "PendingUpgrade retained for retry on next service start. " +
                    "If PreviousVersion exists, old files are preserved for manual recovery.",
                    Keywords.Telemetry);
                return UpgradeResult.Failed;
            }
        }

        private static bool IsSkippedFile(string relativePath)
        {
            return IsMarkerFile(relativePath) ||
                   string.Equals(relativePath, ServiceExeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPriorityExe(string relativePath)
        {
            foreach (string exe in PriorityExes)
            {
                if (PathComparer.Equals(relativePath, exe))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMarkerFile(string relativePath)
        {
            return string.Equals(relativePath, ReadyMarkerFileName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(relativePath, Phase1CompleteMarkerFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the given GVFS.Mount.exe path is under the install
        /// directory — either in the flat layout (<c>{app}\GVFS.Mount.exe</c>)
        /// or in a versioned subdirectory (<c>{app}\Versions\*\GVFS.Mount.exe</c>
        /// or <c>{app}\Current\GVFS.Mount.exe</c>).
        /// </summary>
        private static bool IsInstalledMountPath(string installDir, string processPath)
        {
            // Verify filename is actually GVFS.Mount.exe (not some other exe
            // under the install dir).
            if (!PathComparer.Equals(Path.GetFileName(processPath), MountExeName))
            {
                return false;
            }

            // Verify the exe lives under the install directory.
            string normalizedInstallDir = installDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return processPath.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Moves a file, retrying once after killing any process that has the
        /// source file locked (e.g. a GVFS process that started mid-upgrade).
        /// </summary>
        private static void MoveFileWithRetry(ITracer tracer, string source, string dest)
        {
            try
            {
                File.Move(source, dest);
            }
            catch (IOException)
            {
                if (TryKillProcessByPath(tracer, source))
                {
                    Thread.Sleep(1000);
                    File.Move(source, dest);
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Returns relative paths ordered for removal: priority exes first
        /// (so no new GVFS processes can start), then everything else.
        /// Skips marker files and GVFS.Service.exe.
        /// </summary>
        private static List<string> OrderForRemoval(string[] absolutePaths, string baseDir)
        {
            List<string> rest = new List<string>();
            HashSet<string> present = new HashSet<string>(PathComparer);

            foreach (string fullPath in absolutePaths)
            {
                string relativePath = fullPath.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar);
                if (IsSkippedFile(relativePath))
                {
                    continue;
                }

                if (IsPriorityExe(relativePath))
                {
                    present.Add(relativePath);
                }
                else
                {
                    rest.Add(relativePath);
                }
            }

            List<string> ordered = new List<string>();
            foreach (string exe in PriorityExes)
            {
                if (present.Contains(exe))
                {
                    ordered.Add(exe);
                }
            }

            ordered.AddRange(rest);
            return ordered;
        }

        /// <summary>
        /// Returns relative paths ordered for install: reverse of removal order
        /// so priority exes are replaced last (all supporting files in place
        /// before any GVFS process can start).
        /// </summary>
        private static List<string> OrderForInstall(string[] absolutePaths, string baseDir)
        {
            List<string> ordered = OrderForRemoval(absolutePaths, baseDir);
            ordered.Reverse();
            return ordered;
        }

        /// <summary>
        /// Finds and kills any process whose main module matches the given
        /// file path. Returns true if a process was found and killed.
        /// </summary>
        private static bool TryKillProcessByPath(ITracer tracer, string filePath)
        {
            bool killed = false;
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        if (PathComparer.Equals(process.MainModule?.FileName, filePath))
                        {
                            tracer.RelatedWarning(
                                $"{nameof(PendingUpgradeHandler)}: Killing process {process.ProcessName} " +
                                $"(PID {process.Id}) that is locking {filePath}");
                            process.Kill();
                            process.WaitForExit(5000);
                            killed = true;
                        }
                    }
                    catch (Exception)
                    {
                        // Access denied or process already exited — skip
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                tracer.RelatedWarning($"{nameof(PendingUpgradeHandler)}: Error enumerating processes: {ex.Message}");
            }

            return killed;
        }

        private static string TryGetOldVersion(string previousVersionDir)
        {
            try
            {
                string oldGvfsExe = Path.Combine(previousVersionDir, "GVFS.exe");
                if (File.Exists(oldGvfsExe))
                {
                    return FileVersionInfo.GetVersionInfo(oldGvfsExe).ProductVersion;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void TryRestoreFromPreviousVersion(ITracer tracer, string previousVersionDir, string installDir)
        {
            // Move any backed-up files back to the install directory so we
            // can retry Phase 1 cleanly.
            try
            {
                foreach (string backupFile in Directory.GetFiles(previousVersionDir, "*", SearchOption.AllDirectories))
                {
                    string relativePath = backupFile.Substring(previousVersionDir.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (IsMarkerFile(relativePath))
                    {
                        continue;
                    }

                    string installedFile = Path.Combine(installDir, relativePath);
                    if (!File.Exists(installedFile))
                    {
                        File.Move(backupFile, installedFile);
                    }
                }
            }
            catch (Exception ex)
            {
                tracer.RelatedWarning(
                    $"{nameof(PendingUpgradeHandler)}: Failed to restore from PreviousVersion: {ex.Message}",
                    Keywords.Telemetry);
            }
        }

        private static void TryDeleteDirectory(ITracer tracer, string path, string description)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Removed {description} directory");
            }
            catch (Exception ex)
            {
                tracer.RelatedWarning($"{nameof(PendingUpgradeHandler)}: Failed to remove {description} directory: {ex.Message}");
            }
        }
    }

    public enum UpgradeResult
    {
        NoPending,
        Applied,
        DeferredMountsRunning,
        NotReady,
        Failed,
    }
}
