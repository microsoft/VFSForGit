using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    public class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool MoveFile(string lpExistingFileName, string lpNewFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            [In] string lpFileName,
            uint dwDesiredAccess,
            FileShare dwShareMode,
            [In] IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)]FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes,
            [In] IntPtr hTemplateFile);
    }
}
