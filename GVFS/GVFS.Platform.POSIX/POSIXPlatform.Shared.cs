using GVFS.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.POSIX
{
    public abstract partial class POSIXPlatform
    {
        public static bool IsElevatedImplementation()
        {
            uint euid = GetEuid();
            return euid == 0;
        }

        public static bool IsProcessActiveImplementation(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot, string dotGVFSRoot)
        {
            // Pipes are stored as files on POSIX, use a rooted pipe name to keep full control of the location of the file
            return Path.Combine(enlistmentRoot, dotGVFSRoot, "GVFS_NetCorePipe");
        }

        public static bool IsConsoleOutputRedirectedToFileImplementation()
        {
            // TODO(#1355): Implement proper check
            return false;
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, string dotGVFSRoot, out string enlistmentRoot, out string errorMessage)
        {
            enlistmentRoot = null;

            string finalDirectory;
            if (!POSIXFileSystem.TryGetNormalizedPathImplementation(directory, out finalDirectory, out errorMessage))
            {
                return false;
            }

            enlistmentRoot = Paths.GetRoot(finalDirectory, dotGVFSRoot);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {dotGVFSRoot} in {finalDirectory}";
                return false;
            }

            return true;
        }

        [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
        private static extern uint GetEuid();
    }
}
