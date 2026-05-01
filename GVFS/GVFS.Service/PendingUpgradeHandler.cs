using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Service
{
    /// <summary>
    /// Detects and applies staged upgrades from the PendingUpgrade directory.
    ///
    /// When the installer runs with mounts active, it stages new files to
    /// {installDir}\PendingUpgrade\ instead of replacing files in-place.
    /// On service start (before automount), if no GVFS.Mount processes are
    /// running, this class applies the upgrade using atomic moves:
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
        private const string PendingUpgradeDirectoryName = "PendingUpgrade";
        private const string PreviousVersionDirectoryName = "PreviousVersion";

        /// <summary>
        /// Checks for and applies a pending staged upgrade. Returns true if
        /// an upgrade was applied (caller should proceed with normal startup).
        /// </summary>
        public static bool TryApplyPendingUpgrade(ITracer tracer)
        {
            string installDir = Configuration.AssemblyPath;
            string pendingUpgradeDir = Path.Combine(installDir, PendingUpgradeDirectoryName);
            string previousVersionDir = Path.Combine(installDir, PreviousVersionDirectoryName);

            if (!Directory.Exists(pendingUpgradeDir))
            {
                // No pending upgrade. Clean up PreviousVersion if it exists
                // (leftover from a completed upgrade where cleanup was interrupted).
                TryDeleteDirectory(tracer, previousVersionDir, "leftover PreviousVersion");
                return false;
            }

            tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Pending upgrade detected at {pendingUpgradeDir}");

            // Don't apply if GVFS.Mount processes are still running — their
            // executables are locked and moves would fail. Upgrade will be
            // retried on next service start when no mounts are active.
            Process[] mountProcesses = Array.Empty<Process>();
            try
            {
                mountProcesses = Process.GetProcessesByName("GVFS.Mount");
                if (mountProcesses.Length > 0)
                {
                    tracer.RelatedWarning(
                        $"{nameof(PendingUpgradeHandler)}: {mountProcesses.Length} GVFS.Mount process(es) still running. " +
                        "Deferring upgrade until no mounts are active.");
                    return false;
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
                // Phase 1: Move old files to PreviousVersion (backup for rollback)
                // If PreviousVersion already exists, a prior attempt was interrupted
                // after phase 1 but before phase 2 completed. Skip to phase 2.
                string[] stagedFiles = Directory.GetFiles(pendingUpgradeDir, "*", SearchOption.AllDirectories);
                if (!Directory.Exists(previousVersionDir))
                {
                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 - backing up {stagedFiles.Length} file(s) to PreviousVersion");

                    int backedUp = 0;
                    foreach (string sourceFile in stagedFiles)
                    {
                        string relativePath = sourceFile.Substring(pendingUpgradeDir.Length).TrimStart(Path.DirectorySeparatorChar);

                        // Skip GVFS.Service.exe — locked by this running process,
                        // already replaced in-place by the installer.
                        if (string.Equals(relativePath, "GVFS.Service.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string installedFile = Path.Combine(installDir, relativePath);
                        if (File.Exists(installedFile))
                        {
                            string backupFile = Path.Combine(previousVersionDir, relativePath);
                            string backupDir = Path.GetDirectoryName(backupFile);
                            if (!Directory.Exists(backupDir))
                            {
                                Directory.CreateDirectory(backupDir);
                            }

                            File.Move(installedFile, backupFile);
                            backedUp++;
                        }
                    }

                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 complete. Backed up {backedUp} file(s)");
                }
                else
                {
                    tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 1 already done (PreviousVersion exists). Resuming phase 2.");
                }

                // Phase 2: Move new files from PendingUpgrade to install dir
                tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Phase 2 - applying {stagedFiles.Length} staged file(s)");

                int applied = 0;
                int skipped = 0;
                foreach (string sourceFile in stagedFiles)
                {
                    string relativePath = sourceFile.Substring(pendingUpgradeDir.Length).TrimStart(Path.DirectorySeparatorChar);

                    if (string.Equals(relativePath, "GVFS.Service.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

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

                    File.Move(sourceFile, destFile);
                    applied++;
                }

                tracer.RelatedInfo(
                    $"{nameof(PendingUpgradeHandler)}: Phase 2 complete. " +
                    $"Applied={applied}, Skipped={skipped}");

                // Phase 3: Clean up
                // Delete the skipped GVFS.Service.exe from PendingUpgrade first,
                // otherwise Directory.Delete will fail on the non-empty directory.
                string skippedServiceExe = Path.Combine(pendingUpgradeDir, "GVFS.Service.exe");
                if (File.Exists(skippedServiceExe))
                {
                    File.Delete(skippedServiceExe);
                }

                TryDeleteDirectory(tracer, pendingUpgradeDir, "PendingUpgrade");
                TryDeleteDirectory(tracer, previousVersionDir, "PreviousVersion");

                tracer.RelatedInfo($"{nameof(PendingUpgradeHandler)}: Upgrade complete");
                return true;
            }
            catch (Exception ex)
            {
                tracer.RelatedError(
                    $"{nameof(PendingUpgradeHandler)}: Upgrade failed: {ex.Message}. " +
                    "PendingUpgrade retained for retry on next service start. " +
                    "If PreviousVersion exists, old files are preserved for manual recovery.");
                return false;
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
}
