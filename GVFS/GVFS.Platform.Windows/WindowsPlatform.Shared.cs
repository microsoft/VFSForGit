using GVFS.Common;
using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GVFS.Platform.Windows
{
    public partial class WindowsPlatform
    {
        public const string InstallerExtension = "exe";

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

        public static bool IsProcessActiveImplementation(int processId)
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

                return false;
            }
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            return "GVFS_" + enlistmentRoot.ToUpper().Replace(':', '_');
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

            enlistmentRoot = Paths.GetRoot(finalDirectory, GVFSConstants.DotGVFS.Root);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {GVFSConstants.DotGVFS.Root} in {finalDirectory}";
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
