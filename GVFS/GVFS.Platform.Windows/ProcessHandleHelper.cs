using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Windows
{
    /// <summary>
    /// Race-free liveness checks against a Win32 process handle.
    /// Unlike <see cref="System.Diagnostics.Process.GetProcessById(int)"/>,
    /// these helpers query an already-opened handle, so they cannot lose the
    /// race where the target exits between the caller starting it and the
    /// caller looking it up, and they cannot alias a reused PID. The kernel
    /// keeps the process object alive for as long as the handle is held,
    /// even after the child has exited and its PID has been reused.
    /// </summary>
    public static class ProcessHandleHelper
    {
        // From WinBase.h. WaitForSingleObject signals the process handle
        // immediately when the process has exited; we pass a zero timeout
        // so we never block.
        private const uint WaitObject0 = 0x00000000;
        private const uint WaitTimeout = 0x00000102;

        /// <summary>
        /// Returns <c>true</c> if the process has exited. Uses a non-blocking
        /// wait so it's safe to call from a polling loop.
        /// </summary>
        public static bool HasExited(SafeProcessHandle handle)
        {
            uint result = WaitForSingleObject(handle, 0);
            return result == WaitObject0;
        }

        /// <summary>
        /// Reads the process's exit code. Only meaningful after
        /// <see cref="HasExited"/> returns <c>true</c>. Returns <c>false</c>
        /// if the underlying Win32 call fails.
        /// </summary>
        public static bool TryGetExitCode(SafeProcessHandle handle, out int exitCode)
        {
            uint code;
            if (GetExitCodeProcess(handle, out code))
            {
                exitCode = unchecked((int)code);
                return true;
            }

            exitCode = -1;
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeoutMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint exitCode);
    }
}
