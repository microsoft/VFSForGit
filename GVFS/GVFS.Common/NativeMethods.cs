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
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3;

        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900a8;

        private const int ReparseDataPathBufferLength = 1000;

        private const int ERROR_FILE_NOT_FOUND = 0x2;
        private const int ERROR_PATH_NOT_FOUND = 0x3;

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
                    ThrowLastWin32Exception($"Invalid handle for '{path}'");
                }

                if (!FlushFileBuffers(fileHandle))
                {
                    ThrowLastWin32Exception($"Failed to flush buffers for '{path}'");
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
                ThrowLastWin32Exception($"Failed to get volume information for '{volumeRoot}'");
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
                ThrowLastWin32Exception($"Failed to move '{existingFileName}' to '{newFileName}'");
            }
        }

        public static void SetDirectoryLastWriteTime(string path, DateTime lastWriteTime, out bool directoryExists)
        {
            // We can't use Directory.SetLastWriteTime as it requests GENERIC_WRITE access
            // which will fail for directory placeholders.  The only access requried by SetFileTime
            // is FILE_WRITE_ATTRIBUTES (which ProjFS does allow for placeholders)

            using (SafeFileHandle handle =
                CreateFile(
                    path,
                    FileAccess.FILE_WRITE_ATTRIBUTES,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    FileAttributes.FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
                    {
                        directoryExists = false;
                        return;
                    }

                    throw new Win32Exception(error, $"{nameof(SetDirectoryLastWriteTime)}: Failed to open handle for '{path}'");
                }

                // SetFileTime will not update times with value 0
                long creationFileTime = 0;
                long lastAccessFileTime = 0;
                long lastWriteFileTime = lastWriteTime.ToFileTime();
                if (!SetFileTime(handle, ref creationFileTime, ref lastAccessFileTime, ref lastWriteFileTime))
                {
                    ThrowLastWin32Exception($"{nameof(SetDirectoryLastWriteTime)}: Failed to update last write time for '{path}'");
                }
            }

            directoryExists = true;
            return;
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
                ThrowLastWin32Exception($"Failed to get OS version info");
            }

            return versionInfo.BuildNumber;
        }

        public static bool IsSymLink(string path)
        {
            using (SafeFileHandle output = CreateFile(
                path,
                FileAccess.FILE_READ_ATTRIBUTES,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileAttributes.FILE_FLAG_BACKUP_SEMANTICS | FileAttributes.FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero))
            {
                if (output.IsInvalid)
                {
                    ThrowLastWin32Exception($"Invalid handle for '{path}' as symlink");
                }

                REPARSE_DATA_BUFFER reparseData = new REPARSE_DATA_BUFFER();
                reparseData.ReparseDataLength = (4 * sizeof(ushort)) + ReparseDataPathBufferLength;
                uint bytesReturned;
                if (!DeviceIoControl(output, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, out reparseData, (uint)Marshal.SizeOf(reparseData), out bytesReturned, IntPtr.Zero))
                {
                    ThrowLastWin32Exception($"Failed to place reparse point for '{path}'");
                }

                return reparseData.ReparseTag == IO_REPARSE_TAG_SYMLINK || reparseData.ReparseTag == IO_REPARSE_TAG_MOUNT_POINT;
            }
        }

        public static DateTime GetLastRebootTime()
        {
            // GetTickCount64 is a native call and returns the number
            // of milliseconds since the system was started (and not DateTime.Ticks).
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ms724411.aspx
            TimeSpan uptime = TimeSpan.FromMilliseconds(GetTickCount64());
            return DateTime.Now - uptime;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(
            string existingFileName,
            string newFileName,
            uint flags);

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

        // For use with FSCTL_GET_REPARSE_POINT
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint IoControlCode,
            [In] IntPtr InBuffer,
            uint nInBufferSize,
            [Out] out REPARSE_DATA_BUFFER OutBuffer,
            uint nOutBufferSize,
            out uint pBytesReturned,
            [In] IntPtr Overlapped);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileTime(
            SafeFileHandle hFile,
            [In] ref long creationTime,
            [In] ref long lastAccessTime,
            [In] ref long lastWriteTime);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct REPARSE_DATA_BUFFER
        {
            public uint ReparseTag;
            public ushort ReparseDataLength;
            public ushort Reserved;
            public ushort SubstituteNameOffset;
            public ushort SubstituteNameLength;
            public ushort PrintNameOffset;
            public ushort PrintNameLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = ReparseDataPathBufferLength)]
            public byte[] PathBuffer;
        }

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
