using GVFS.Common;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GVFS.Platform.Windows
{
    public partial class WindowsPlatform
    {
        public const string DotGVFSRoot = ".gvfs";
        public const string UpgradeConfirmMessage = "`gvfs upgrade --confirm`";

        private const int StillActive = 259; /* from Win32 STILL_ACTIVE */

        private const int ErrorAccessDenied = 5;         /* ERROR_ACCESS_DENIED */
        private const int ErrorInvalidParameter = 87;    /* ERROR_INVALID_PARAMETER */

        public static bool IsElevatedImplementation()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool IsProcessActiveImplementation(int processId, bool tryGetProcessById)
        {
            using (SafeFileHandle process = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, processId))
            {
                if (!process.IsInvalid)
                {
                    uint exitCode;
                    if (NativeMethods.GetExitCodeProcess(process, out exitCode) && exitCode == StillActive)
                    {
                        return true;
                    }
                }
                else if (tryGetProcessById)
                {
                    // The process.IsInvalid may be true when the mount process doesn't have access to call
                    // OpenProcess for the specified processId. Fallback to slow way of finding process.
                    try
                    {
                        Process.GetProcessById(processId);
                        return true;
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Attempts to read a process's creation timestamp (raw FILETIME, 100-ns ticks since 1601)
        /// for identity comparison. The value written to <paramref name="startTime"/> is only valid
        /// when the return value is <see cref="ProcessStartTimeResult.Success"/>; two calls for the
        /// same underlying process yield equal values, and a recycled PID yields a different value.
        /// The failure values are deliberately distinct so that the orphan-lock detector can release
        /// a lock only on positive evidence (see <see cref="ProcessStartTimeResult"/>).
        /// </summary>
        public static ProcessStartTimeResult TryGetActiveProcessStartTimeImplementation(int processId, out long startTime)
        {
            startTime = 0;

            using (SafeFileHandle process = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, processId))
            {
                if (process.IsInvalid)
                {
                    // Classify the failure by the Win32 error so callers can distinguish "the holder
                    // is gone" from "we could not tell". OpenProcess sets last error on failure and
                    // the P/Invoke is declared SetLastError=true, so the value is preserved here.
                    int error = Marshal.GetLastWin32Error();
                    switch (error)
                    {
                        case ErrorInvalidParameter:
                            // No process exists with this PID.
                            return ProcessStartTimeResult.ProcessNotFound;

                        case ErrorAccessDenied:
                            // A process exists but we cannot open it. Because we only reach the
                            // identity check for holders we successfully opened at acquire time,
                            // and OpenProcess(QueryLimitedInformation) access is stable for a
                            // process's lifetime, this means the PID now refers to a different
                            // process than the original holder.
                            return ProcessStartTimeResult.Inaccessible;

                        default:
                            // Transient/unclassified (e.g. ERROR_NOT_ENOUGH_MEMORY). We do not know
                            // whether the original holder is alive.
                            return ProcessStartTimeResult.Indeterminate;
                    }
                }

                // GetProcessTimes succeeds for terminated processes whose kernel object still
                // exists (e.g., an outstanding handle elsewhere). Confirm the process is still
                // running before trusting the creation time as an identity marker.
                if (!NativeMethods.GetExitCodeProcess(process, out uint exitCode))
                {
                    return ProcessStartTimeResult.Indeterminate;
                }

                if (exitCode != StillActive)
                {
                    // The process was opened but has already exited.
                    return ProcessStartTimeResult.ProcessNotFound;
                }

                if (!NativeMethods.GetProcessTimes(process, out long creationTime, out _, out _, out _))
                {
                    return ProcessStartTimeResult.Indeterminate;
                }

                startTime = creationTime;
                return ProcessStartTimeResult.Success;
            }
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            return "GVFS_" + enlistmentRoot.ToUpper().Replace(':', '_');
        }

        public static string GetSecureDataRootForGVFSImplementation()
        {
            string envOverride = Environment.GetEnvironmentVariable("GVFS_SECURE_DATA_ROOT");
            if (!string.IsNullOrEmpty(envOverride))
            {
                return envOverride;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.Create),
                 "GVFS",
                 "ProgramData");
        }

        public static string GetCommonAppDataRootForGVFSImplementation()
        {
            string envOverride = Environment.GetEnvironmentVariable("GVFS_COMMON_APPDATA_ROOT");
            if (!string.IsNullOrEmpty(envOverride))
            {
                return envOverride;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "GVFS");
        }

        public static string GetLogsDirectoryForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(
                GetCommonAppDataRootForGVFSImplementation(),
                componentName,
                "Logs");
        }

        public static string GetSecureDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetSecureDataRootForGVFSImplementation(), componentName);
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            enlistmentRoot = null;

            string finalDirectory;
            if (!WindowsFileSystem.TryGetNormalizedPathImplementation(directory, out finalDirectory, out errorMessage))
            {
                return false;
            }

            enlistmentRoot = Paths.GetRoot(finalDirectory, DotGVFSRoot);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {DotGVFSRoot} in {finalDirectory}";
                return false;
            }

            return true;
        }
    }
}
