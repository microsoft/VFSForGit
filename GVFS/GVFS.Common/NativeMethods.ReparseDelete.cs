using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Common
{
    public static partial class NativeMethods
    {
        private const int ERROR_NO_MORE_FILES = 0x12;

        // FILE_INFO_BY_HANDLE_CLASS.FileFullDirectoryInfo
        private const int FileFullDirectoryInfo = 14;

        // Offsets into FILE_FULL_DIR_INFO.
        private const int FileFullDirInfo_NextEntryOffset = 0;
        private const int FileFullDirInfo_FileAttributes = 56;
        private const int FileFullDirInfo_FileNameLength = 60;
        private const int FileFullDirInfo_FileName = 68;

        /// <summary>
        /// Recursively deletes a directory tree that may contain ProjFS placeholders WITHOUT
        /// recalling their content through the ProjFS provider. Every handle is opened with
        /// FILE_FLAG_OPEN_REPARSE_POINT, so the reparse points are enumerated and deleted directly
        /// instead of being materialized by the provider. This avoids the
        /// "provider ... temporarily unavailable" failures that occur when the provider is busy
        /// (e.g. under heavy concurrent ProjFS load) or is no longer projecting the moved
        /// placeholders (as is the case for a dehydrate backup folder).
        /// </summary>
        public static void DeleteDirectoryWithoutProviderRecall(string path)
        {
            foreach (DirectoryEntry entry in EnumerateReparsePointAware(path))
            {
                string childPath = Path.Combine(path, entry.Name);
                if ((entry.Attributes & (uint)FileAttributes.FILE_ATTRIBUTE_DIRECTORY) != 0)
                {
                    DeleteDirectoryWithoutProviderRecall(childPath);
                }
                else
                {
                    DeleteFileReparsePointAware(childPath, entry.Attributes);
                }
            }

            // The directory is now physically empty; delete it as a reparse point.
            DeleteEmptyDirectoryReparsePointAware(path);
        }

        private static IEnumerable<DirectoryEntry> EnumerateReparsePointAware(string path)
        {
            List<DirectoryEntry> results = new List<DirectoryEntry>();

            FileAccess access = (FileAccess)((uint)FileAccess.FILE_LIST_DIRECTORY | (uint)FileAccess.FILE_READ_ATTRIBUTES);
            FileAttributes flags = (FileAttributes)((uint)FileAttributes.FILE_FLAG_BACKUP_SEMANTICS | (uint)FileAttributes.FILE_FLAG_OPEN_REPARSE_POINT);

            using (SafeFileHandle directory = CreateFile(
                path,
                access,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                flags,
                IntPtr.Zero))
            {
                if (directory.IsInvalid)
                {
                    ThrowLastWin32Exception($"Failed to open directory for enumeration: {path}");
                }

                const int BufferSize = 64 * 1024;
                IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
                try
                {
                    while (GetFileInformationByHandleEx(directory, FileFullDirectoryInfo, buffer, (uint)BufferSize))
                    {
                        IntPtr current = buffer;
                        while (true)
                        {
                            uint nextOffset = (uint)Marshal.ReadInt32(current, FileFullDirInfo_NextEntryOffset);
                            uint fileAttributes = (uint)Marshal.ReadInt32(current, FileFullDirInfo_FileAttributes);
                            int fileNameLength = Marshal.ReadInt32(current, FileFullDirInfo_FileNameLength);
                            string name = Marshal.PtrToStringUni(IntPtr.Add(current, FileFullDirInfo_FileName), fileNameLength / 2);

                            if (name != "." && name != "..")
                            {
                                results.Add(new DirectoryEntry(name, fileAttributes));
                            }

                            if (nextOffset == 0)
                            {
                                break;
                            }

                            current = IntPtr.Add(current, (int)nextOffset);
                        }
                    }

                    int error = Marshal.GetLastWin32Error();
                    if (error != ERROR_NO_MORE_FILES)
                    {
                        throw new Win32Exception(error, $"Failed to enumerate directory: {path}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return results;
        }

        private static void DeleteFileReparsePointAware(string path, uint attributes)
        {
            // FILE_FLAG_DELETE_ON_CLOSE fails on read-only files, so clear the attribute first.
            // Setting attributes does not recall placeholder content.
            if ((attributes & (uint)FileAttributes.FILE_ATTRIBUTE_READONLY) != 0)
            {
                uint newAttributes = attributes & ~(uint)FileAttributes.FILE_ATTRIBUTE_READONLY;
                if (newAttributes == 0)
                {
                    newAttributes = (uint)FileAttributes.FILE_ATTRIBUTE_NORMAL;
                }

                SetFileAttributesW(path, newAttributes);
            }

            FileAttributes flags = (FileAttributes)((uint)FileAttributes.FILE_FLAG_OPEN_REPARSE_POINT | (uint)FileAttributes.FILE_FLAG_DELETE_ON_CLOSE);
            using (SafeFileHandle handle = CreateFile(
                path,
                FileAccess.DELETE,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                flags,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    ThrowLastWin32Exception($"Failed to open file for deletion: {path}");
                }
            }
        }

        private static void DeleteEmptyDirectoryReparsePointAware(string path)
        {
            FileAttributes flags = (FileAttributes)((uint)FileAttributes.FILE_FLAG_BACKUP_SEMANTICS | (uint)FileAttributes.FILE_FLAG_OPEN_REPARSE_POINT | (uint)FileAttributes.FILE_FLAG_DELETE_ON_CLOSE);
            using (SafeFileHandle handle = CreateFile(
                path,
                FileAccess.DELETE,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                flags,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    ThrowLastWin32Exception($"Failed to open directory for deletion: {path}");
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            int fileInformationClass,
            IntPtr lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

        private readonly struct DirectoryEntry
        {
            public DirectoryEntry(string name, uint attributes)
            {
                this.Name = name;
                this.Attributes = attributes;
            }

            public string Name { get; }

            public uint Attributes { get; }
        }
    }
}
