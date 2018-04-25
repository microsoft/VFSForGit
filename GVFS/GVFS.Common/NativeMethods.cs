using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Common
{
    public static class NativeMethods
    {
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3;

        public enum FileAttributes : uint
        {
            FILE_ATTRIBUTE_READONLY            = 1,
            FILE_ATTRIBUTE_HIDDEN              = 2,
            FILE_ATTRIBUTE_SYSTEM              = 4,
            FILE_ATTRIBUTE_DIRECTORY           = 16,
            FILE_ATTRIBUTE_ARCHIVE             = 32,
            FILE_ATTRIBUTE_DEVICE              = 64,
            FILE_ATTRIBUTE_NORMAL              = 128,
            FILE_ATTRIBUTE_TEMPORARY           = 256,
            FILE_ATTRIBUTE_SPARSEFILE          = 512,
            FILE_ATTRIBUTE_REPARSEPOINT        = 1024,
            FILE_ATTRIBUTE_COMPRESSED          = 2048,
            FILE_ATTRIBUTE_OFFLINE             = 4096,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 8192,
            FILE_ATTRIBUTE_ENCRYPTED           = 16384,
            FILE_FLAG_FIRST_PIPE_INSTANCE      = 524288,
            FILE_FLAG_OPEN_NO_RECALL           = 1048576,
            FILE_FLAG_OPEN_REPARSE_POINT       = 2097152,
            FILE_FLAG_POSIX_SEMANTICS          = 16777216,
            FILE_FLAG_BACKUP_SEMANTICS         = 33554432,
            FILE_FLAG_DELETE_ON_CLOSE          = 67108864,
            FILE_FLAG_SEQUENTIAL_SCAN          = 134217728,
            FILE_FLAG_RANDOM_ACCESS            = 268435456,
            FILE_FLAG_NO_BUFFERING             = 536870912,
            FILE_FLAG_OVERLAPPED               = 1073741824,
            FILE_FLAG_WRITE_THROUGH            = 2147483648
        }

        public enum FileAccess : uint
        {
            FILE_READ_DATA            = 1,
            FILE_LIST_DIRECTORY       = 1,
            FILE_WRITE_DATA           = 2,
            FILE_ADD_FILE             = 2,
            FILE_APPEND_DATA          = 4,
            FILE_ADD_SUBDIRECTORY     = 4,
            FILE_CREATE_PIPE_INSTANCE = 4,
            FILE_READ_EA              = 8,
            FILE_WRITE_EA             = 16,
            FILE_EXECUTE              = 32,
            FILE_TRAVERSE             = 32,
            FILE_DELETE_CHILD         = 64,
            FILE_READ_ATTRIBUTES      = 128,
            FILE_WRITE_ATTRIBUTES     = 256,
            SPECIFIC_RIGHTS_ALL       = 65535,
            DELETE                    = 65536,
            READ_CONTROL              = 131072,
            STANDARD_RIGHTS_READ      = 131072,
            STANDARD_RIGHTS_WRITE     = 131072,
            STANDARD_RIGHTS_EXECUTE   = 131072,
            WRITE_DAC                 = 262144,
            WRITE_OWNER               = 524288,
            STANDARD_RIGHTS_REQUIRED  = 983040,
            SYNCHRONIZE               = 1048576,
            FILE_GENERIC_READ         = 1179785,
            FILE_GENERIC_EXECUTE      = 1179808,
            FILE_GENERIC_WRITE        = 1179926,
            STANDARD_RIGHTS_ALL       = 2031616,
            FILE_ALL_ACCESS           = 2032127,
            ACCESS_SYSTEM_SECURITY    = 16777216,
            MAXIMUM_ALLOWED           = 33554432,
            GENERIC_ALL               = 268435456,
            GENERIC_EXECUTE           = 536870912,
            GENERIC_WRITE             = 1073741824,
            GENERIC_READ              = 2147483648
        }

        [Flags]
        public enum MoveFileFlags : uint
        {
            MoveFileReplaceExisting = 0x00000001,    // MOVEFILE_REPLACE_EXISTING
            MoveFileCopyAllowed = 0x00000002,        // MOVEFILE_COPY_ALLOWED           
            MoveFileDelayUntilReboot = 0x00000004,   // MOVEFILE_DELAY_UNTIL_REBOOT     
            MoveFileWriteThrough = 0x00000008,       // MOVEFILE_WRITE_THROUGH          
            MoveFileCreateHardlink = 0x00000010,     // MOVEFILE_CREATE_HARDLINK        
            MoveFileFailIfNotTrackable = 0x00000020, // MOVEFILE_FAIL_IF_NOT_TRACKABLE  
        }

        [Flags]
        public enum FileSystemFlags : uint
        {
            FILE_RETURNS_CLEANUP_RESULT_INFO = 0x00000200
        }

        /// <summary>
        /// Lock specified directory, so it can't be deleted or renamed by any other process
        /// The trick is to open a handle on the directory (FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT) 
        /// and keep it. If it is a junction the second option is required, and if it is a standard directory it is ignored.
        /// Caller must call Close() or Dispose() on the returned safe handle to release the lock
        /// </summary>
        /// <param name="path">Path to existing directory junction</param>
        /// <returns>SafeFileHandle</returns>
        public static SafeFileHandle LockDirectory(string path)
        {
            SafeFileHandle result = CreateFile(
                path,
                FileAccess.GENERIC_READ,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileAttributes.FILE_FLAG_BACKUP_SEMANTICS | FileAttributes.FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);
            if (result.IsInvalid)
            {
                ThrowLastWin32Exception();
            }

            return result;
        }

        public static void FlushFileBuffers(string path)
        {
            using (SafeFileHandle fileHandle = CreateFile(
                path,
                FileAccess.GENERIC_WRITE,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                FileAttributes.FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero))
            {
                if (fileHandle.IsInvalid)
                {
                    ThrowLastWin32Exception();
                }

                if (!FlushFileBuffers(fileHandle))
                {
                    ThrowLastWin32Exception();
                }
            }
        }

        public static bool IsFeatureSupportedByVolume(string volumeRoot, FileSystemFlags flags)
        {
            uint volumeSerialNumber;
            uint maximumComponentLength;
            uint fileSystemFlags;

            if (!GetVolumeInformation(
                volumeRoot,
                null,
                0,
                out volumeSerialNumber,
                out maximumComponentLength,
                out fileSystemFlags,
                null,
                0))
            {
                ThrowLastWin32Exception();
            }

            return (fileSystemFlags & (uint)flags) == (uint)flags;
        }

        public static uint FlushTraceLogger(string sessionName, string sessionGuid, out string logfileName)
        {
            EventTraceProperties properties = new EventTraceProperties();
            properties.Wnode.BufferSize = (uint)Marshal.SizeOf(properties);
            properties.Wnode.Guid = new Guid(sessionGuid);
            properties.LoggerNameOffset = (uint)Marshal.OffsetOf(typeof(EventTraceProperties), "LoggerName");
            properties.LogFileNameOffset = (uint)Marshal.OffsetOf(typeof(EventTraceProperties), "LogFileName");
            uint result = ControlTrace(0, sessionName, ref properties, EVENT_TRACE_CONTROL_FLUSH);
            logfileName = properties.LogFileName;
            return result;
        }

        public static void MoveFile(string existingFileName, string newFileName, MoveFileFlags flags)
        {
            if (!MoveFileEx(existingFileName, newFileName, (uint)flags))
            {
                ThrowLastWin32Exception();
            }
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
                    ThrowLastWin32Exception();
                }

                int finalPathSize = GetFinalPathNameByHandle(fileHandle, null, 0, 0);
                StringBuilder finalPath = new StringBuilder(finalPathSize + 1);

                // GetFinalPathNameByHandle buffer size should not include a NULL termination character
                finalPathSize = GetFinalPathNameByHandle(fileHandle, finalPath, finalPathSize, 0);
                if (finalPathSize == 0)
                {
                    ThrowLastWin32Exception();
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

        /// <summary>
        /// Get the build number of the OS
        /// </summary>
        /// <returns>Build number</returns>
        /// <remarks>
        /// For this method to work correctly, the calling application must have a manifest file
        /// that indicates the application supports Windows 10.
        /// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms724451(v=vs.85).aspx for details
        /// </remarks>
        public static uint GetWindowsBuildNumber()
        {
            OSVersionInfo versionInfo = new OSVersionInfo();
            versionInfo.OSVersionInfoSize = (uint)Marshal.SizeOf(versionInfo);
            if (!GetVersionEx(ref versionInfo))
            {
                ThrowLastWin32Exception();
            }

            return versionInfo.BuildNumber;
        }

        private static void ThrowLastWin32Exception()
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

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
        private static extern bool MoveFileEx(
            string existingFileName,
            string newFileName,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            [Out] StringBuilder lpszFilePath,
            int cchFilePath,
            int dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int nFileSystemNameSize);

        [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)]
        private static extern uint ControlTrace(
            [In] ulong sessionHandle,
            [In] string sessionName,
            [In, Out] ref EventTraceProperties properties,
            [In] uint controlCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetVersionEx([In, Out] ref OSVersionInfo versionInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNodeHeader
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public ulong TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EventTraceProperties
        {
            public WNodeHeader Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;

            // "You can use the maximum session name (1024 characters) and maximum log file name (1024 characters) lengths to calculate the buffer size and offsets if not known"
            // https://msdn.microsoft.com/en-us/library/windows/desktop/aa363696(v=vs.85).aspx
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LoggerName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LogFileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVersionInfo
        {
            public uint OSVersionInfoSize;
            public uint MajorVersion;
            public uint MinorVersion;
            public uint BuildNumber;
            public uint PlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CSDVersion;
        }
    }
}
