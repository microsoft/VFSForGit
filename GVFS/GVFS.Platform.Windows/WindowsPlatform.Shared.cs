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

        private enum StdHandle
        {
            Stdin = -10,
            Stdout = -11,
            Stderr = -12
        }

        private enum FileType : uint
        {
            Unknown = 0x0000,
            Disk = 0x0001,
            Char = 0x0002,
            Pipe = 0x0003,
            Remote = 0x8000,
        }

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
        /// Returns true if a process with the given PID is currently active AND writes its
        /// creation timestamp (raw FILETIME, 100-ns ticks since 1601) to <paramref name="startTime"/>.
        /// The returned value is intended for identity comparison only -- two calls for the
        /// same underlying process (no PID reuse) always yield equal values; if the OS recycles
        /// a PID to a new process the value will differ. Returns false (and startTime = 0)
        /// if the process is gone, has terminated (but its kernel object lingers due to an
        /// outstanding handle elsewhere), or cannot be opened for QueryLimitedInformation.
        /// </summary>
        public static bool TryGetActiveProcessStartTimeImplementation(int processId, out long startTime)
        {
            startTime = 0;

            using (SafeFileHandle process = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, processId))
            {
                if (process.IsInvalid)
                {
                    return false;
                }

                // GetProcessTimes succeeds for terminated processes whose kernel object still
                // exists (e.g., an outstanding handle elsewhere). Confirm the process is still
                // running before trusting the creation time as an identity marker.
                if (!NativeMethods.GetExitCodeProcess(process, out uint exitCode) || exitCode != StillActive)
                {
                    return false;
                }

                if (!NativeMethods.GetProcessTimes(process, out long creationTime, out _, out _, out _))
                {
                    return false;
                }

                startTime = creationTime;
                return true;
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

        public static bool IsConsoleOutputRedirectedToFileImplementation()
        {
            return FileType.Disk == GetFileType(GetStdHandle(StdHandle.Stdout));
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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(StdHandle std);

        [DllImport("kernel32.dll")]
        private static extern FileType GetFileType(IntPtr hdl);
    }
}
