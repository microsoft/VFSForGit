using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Common
{
    public static partial class NativeMethods
    {
        public enum FileAttributes : uint
        {
            FILE_ATTRIBUTE_READONLY = 1,
            FILE_ATTRIBUTE_HIDDEN = 2,
            FILE_ATTRIBUTE_SYSTEM = 4,
            FILE_ATTRIBUTE_DIRECTORY = 16,
            FILE_ATTRIBUTE_ARCHIVE = 32,
            FILE_ATTRIBUTE_DEVICE = 64,
            FILE_ATTRIBUTE_NORMAL = 128,
            FILE_ATTRIBUTE_TEMPORARY = 256,
            FILE_ATTRIBUTE_SPARSEFILE = 512,
            FILE_ATTRIBUTE_REPARSEPOINT = 1024,
            FILE_ATTRIBUTE_COMPRESSED = 2048,
            FILE_ATTRIBUTE_OFFLINE = 4096,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 8192,
            FILE_ATTRIBUTE_ENCRYPTED = 16384,
            FILE_FLAG_FIRST_PIPE_INSTANCE = 524288,
            FILE_FLAG_OPEN_NO_RECALL = 1048576,
            FILE_FLAG_OPEN_REPARSE_POINT = 2097152,
            FILE_FLAG_POSIX_SEMANTICS = 16777216,
            FILE_FLAG_BACKUP_SEMANTICS = 33554432,
            FILE_FLAG_DELETE_ON_CLOSE = 67108864,
            FILE_FLAG_SEQUENTIAL_SCAN = 134217728,
            FILE_FLAG_RANDOM_ACCESS = 268435456,
            FILE_FLAG_NO_BUFFERING = 536870912,
            FILE_FLAG_OVERLAPPED = 1073741824,
            FILE_FLAG_WRITE_THROUGH = 2147483648
        }

        public enum FileAccess : uint
        {
            FILE_READ_DATA = 1,
            FILE_LIST_DIRECTORY = 1,
            FILE_WRITE_DATA = 2,
            FILE_ADD_FILE = 2,
            FILE_APPEND_DATA = 4,
            FILE_ADD_SUBDIRECTORY = 4,
            FILE_CREATE_PIPE_INSTANCE = 4,
            FILE_READ_EA = 8,
            FILE_WRITE_EA = 16,
            FILE_EXECUTE = 32,
            FILE_TRAVERSE = 32,
            FILE_DELETE_CHILD = 64,
            FILE_READ_ATTRIBUTES = 128,
            FILE_WRITE_ATTRIBUTES = 256,
            SPECIFIC_RIGHTS_ALL = 65535,
            DELETE = 65536,
            READ_CONTROL = 131072,
            STANDARD_RIGHTS_READ = 131072,
            STANDARD_RIGHTS_WRITE = 131072,
            STANDARD_RIGHTS_EXECUTE = 131072,
            WRITE_DAC = 262144,
            WRITE_OWNER = 524288,
            STANDARD_RIGHTS_REQUIRED = 983040,
            SYNCHRONIZE = 1048576,
            FILE_GENERIC_READ = 1179785,
            FILE_GENERIC_EXECUTE = 1179808,
            FILE_GENERIC_WRITE = 1179926,
            STANDARD_RIGHTS_ALL = 2031616,
            FILE_ALL_ACCESS = 2032127,
            ACCESS_SYSTEM_SECURITY = 16777216,
            MAXIMUM_ALLOWED = 33554432,
            GENERIC_ALL = 268435456,
            GENERIC_EXECUTE = 536870912,
            GENERIC_WRITE = 1073741824,
            GENERIC_READ = 2147483648
        }

        [Flags]
        public enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }

        public static string GetFinalPathName(string path)
        {
            // Using FILE_FLAG_BACKUP_SEMANTICS as it works with file as well as folder path
            // According to MSDN, https://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx,
            // we must set this flag to obtain a handle to a directory
            using (SafeFileHandle fileHandle = CreateFile(
                path,
                FileAccess.FILE_READ_ATTRIBUTES,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                FileAttributes.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero))
            {
                if (fileHandle.IsInvalid)
                {
                    ThrowLastWin32Exception($"Invalid file handle for {path}");
                }

                int finalPathSize = GetFinalPathNameByHandle(fileHandle, null, 0, 0);
                StringBuilder finalPath = new StringBuilder(finalPathSize + 1);

                // GetFinalPathNameByHandle buffer size should not include a NULL termination character
                finalPathSize = GetFinalPathNameByHandle(fileHandle, finalPath, finalPathSize, 0);
                if (finalPathSize == 0)
                {
                    ThrowLastWin32Exception($"Failed to get final path size for {finalPath}");
                }

                string pathString = finalPath.ToString();

                // The remarks section of GetFinalPathNameByHandle mentions the return being prefixed with "\\?\" or "\\?\UNC\"
                // More information the prefixes is here http://msdn.microsoft.com/en-us/library/aa365247(v=VS.85).aspx
                const string PathPrefix = @"\\?\";
                const string UncPrefix = @"\\?\UNC\";
                if (pathString.StartsWith(UncPrefix, StringComparison.Ordinal))
                {
                    pathString = @"\\" + pathString.Substring(UncPrefix.Length);
                }
                else if (pathString.StartsWith(PathPrefix, StringComparison.Ordinal))
                {
                    pathString = pathString.Substring(PathPrefix.Length);
                }

                return pathString;
            }
        }

        public static void ThrowLastWin32Exception(string message)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), message);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle OpenProcess(
            ProcessAccessFlags processAccess,
            bool bInheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeFileHandle hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            [In] string lpFileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            FileShare dwShareMode,
            [In] IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)]FileMode dwCreationDisposition,
            [MarshalAs(UnmanagedType.U4)]FileAttributes dwFlagsAndAttributes,
            [In] IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            [Out] StringBuilder lpszFilePath,
            int cchFilePath,
            int dwFlags);
    }
}
