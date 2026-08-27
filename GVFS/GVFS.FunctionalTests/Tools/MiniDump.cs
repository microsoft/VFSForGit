using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    /// <summary>
    /// Best-effort process minidump writer used to capture post-mortem state of a
    /// (potentially hung) GVFS.Mount process when a functional test fails. Windows
    /// only; a no-op that returns false on other platforms. Never throws.
    /// </summary>
    public static class MiniDump
    {
        [Flags]
        private enum MiniDumpType : uint
        {
            Normal = 0x00000000,
            WithFullMemory = 0x00000002,
            WithHandleData = 0x00000004,
            WithFullMemoryInfo = 0x00000800,
            WithThreadInfo = 0x00001000,
        }

        /// <summary>
        /// Writes a full-memory minidump of the process with the given id to
        /// <paramref name="destinationPath"/>. A full-memory dump is required so
        /// that managed call stacks are resolvable in WinDbg/SOS, which is what we
        /// need to diagnose a mount deadlock. Returns true on success.
        /// </summary>
        public static bool TryWrite(int processId, string destinationPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] MiniDump skipped (non-Windows) for PID {processId}");
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                using (FileStream dumpFile = new FileStream(destinationPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Write))
                {
                    MiniDumpType dumpType =
                        MiniDumpType.WithFullMemory |
                        MiniDumpType.WithHandleData |
                        MiniDumpType.WithThreadInfo |
                        MiniDumpType.WithFullMemoryInfo;

                    bool succeeded = MiniDumpWriteDump(
                        process.Handle,
                        (uint)process.Id,
                        dumpFile.SafeFileHandle,
                        dumpType,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero);

                    if (!succeeded)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Console.Error.WriteLine($"[DIAGNOSTICS] MiniDumpWriteDump failed for PID {processId} (Win32 error {error})");
                        return false;
                    }

                    Console.Error.WriteLine($"[DIAGNOSTICS] Wrote minidump for GVFS.Mount PID {processId} to {destinationPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] Failed to write minidump for PID {processId}: {ex.Message}");
                return false;
            }
        }

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeHandle hFile,
            MiniDumpType dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);
    }
}
