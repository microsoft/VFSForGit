#nullable enable

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Platform.Windows
{
    public sealed partial class WindowsDehydrateBackup
    {
        private static class Native
        {
            private const uint ProjFsTag = 0x9000001C;
            private const uint TombstoneTag = 0xA0000022;
            private const uint DirectoryAttribute = 0x10;
            private const uint ReparseAttribute = 0x400;
            private const uint ReadAttributes = 0x80;
            private const uint DeleteAccess = 0x10000;
            private const uint SynchronizeAccess = 0x100000;
            private const uint OpenReparsePoint = 0x200000;
            private const uint BackupSemantics = 0x2000000;
            private const uint OpenNoRecall = 0x100000;
            private const uint ReturnOnDiskEntriesOnly = 8;
            private const uint RestartScan = 1;
            private const uint OpenExisting = 1;
            private const uint DirectoryFile = 1;
            private const uint NonDirectoryFile = 0x40;
            private const uint SynchronousIoNonAlert = 0x20;
            private const uint OpenForBackupIntent = 0x4000;
            private const uint DispositionDelete = 1;
            private const uint DispositionPosixSemantics = 2;
            private const uint DispositionIgnoreReadonly = 0x10;
            private const int FileAttributeTagInfo = 9;
            private const int FileIdInfo = 18;
            private const int FileDispositionInfoEx = 21;
            private const int FileIdExtdDirectoryInformation = 60;
            private const int DirectoryEntryHeaderSize = 88;
            private const int BufferSize = 64 * 1024;
            private const int StatusNoMoreFiles = unchecked((int)0x80000006);

            public static SafeFileHandle OpenContainer(string path)
            {
                SafeFileHandle handle = CreateFileW(
                    @"\\?\" + path,
                    ReadAttributes | SynchronizeAccess,
                    FileShare.Read,
                    IntPtr.Zero,
                    FileMode.Open,
                    BackupSemantics | OpenReparsePoint | OpenNoRecall,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error, "Failed to open the backup container.");
                }

                return handle;
            }

            public static SafeFileHandle OpenChild(SafeFileHandle parent, string name, bool directory, bool forDeletion = true)
            {
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." ||
                    name.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0)
                {
                    throw new IOException("The directory query returned an invalid child name.");
                }

                IntPtr nameBuffer = Marshal.StringToHGlobalUni(name);
                IntPtr unicodeBuffer = IntPtr.Zero;
                bool parentReferenced = false;
                try
                {
                    unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
                    UnicodeString unicode = new UnicodeString
                    {
                        Length = checked((ushort)(name.Length * sizeof(char))),
                        MaximumLength = checked((ushort)((name.Length + 1) * sizeof(char))),
                        Buffer = nameBuffer,
                    };
                    Marshal.StructureToPtr(unicode, unicodeBuffer, false);
                    parent.DangerousAddRef(ref parentReferenced);
                    ObjectAttributes attributes = new ObjectAttributes
                    {
                        Length = Marshal.SizeOf<ObjectAttributes>(),
                        RootDirectory = parent.DangerousGetHandle(),
                        ObjectName = unicodeBuffer,
                    };
                    // Handle-relative opens cannot be redirected by replacement of a path ancestor.
                    // Deny write/delete sharing while each object is inspected and deleted.
                    uint options = SynchronousIoNonAlert | OpenForBackupIntent | OpenReparsePoint;
                    options |= directory ? DirectoryFile : NonDirectoryFile;
                    int status = NtCreateFile(
                        out SafeFileHandle handle,
                        ReadAttributes | (forDeletion ? DeleteAccess : 0U) | SynchronizeAccess | (directory ? 1U : 0U),
                        ref attributes,
                        out _,
                        IntPtr.Zero,
                        0,
                        FileShare.Read,
                        OpenExisting,
                        options,
                        IntPtr.Zero,
                        0);
                    if (status < 0)
                    {
                        handle?.Dispose();
                        throw StatusException(status, $"Failed to open a physical backup entry (NTSTATUS 0x{status:X8}).");
                    }

                    return handle;
                }
                finally
                {
                    if (parentReferenced)
                    {
                        parent.DangerousRelease();
                    }

                    Marshal.FreeHGlobal(unicodeBuffer);
                    Marshal.FreeHGlobal(nameBuffer);
                }
            }

            public static void VerifyOrdinaryDirectory(SafeFileHandle handle)
            {
                AttributeTag info = GetAttributes(handle);
                if ((info.Attributes & DirectoryAttribute) == 0 ||
                    (info.Attributes & ReparseAttribute) != 0)
                {
                    throw new IOException("The backup scope must be an ordinary directory, not a reparse point.");
                }
            }

            public static void VerifySupportedReparsePoint(SafeFileHandle handle)
            {
                AttributeTag info = GetAttributes(handle);
                if ((info.Attributes & ReparseAttribute) != 0 && info.Tag != ProjFsTag && info.Tag != TombstoneTag)
                {
                    throw new IOException($"Backup cleanup refuses reparse tag 0x{info.Tag:X8}.");
                }
            }

            public static bool IsTombstone(SafeFileHandle handle) => GetAttributes(handle).Tag == TombstoneTag;

            public static FileIdentity GetIdentity(SafeFileHandle handle)
            {
                if (!GetFileInformationByHandleEx(handle, FileIdInfo, out FileIdentity identity, (uint)Marshal.SizeOf<FileIdentity>()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to read the backup entry identity.");
                }

                return identity;
            }

            public static void VerifyFinalPath(SafeFileHandle handle, string expectedPath)
            {
                StringBuilder buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resolve the backup container.");
                }

                if (length >= buffer.Capacity ||
                    !string.Equals(buffer.ToString(), @"\\?\" + expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("The backup container resolves through a different filesystem path.");
                }
            }

            public static IEnumerable<Entry> EnumeratePhysicalEntries(SafeFileHandle directory)
            {
                byte[] buffer = new byte[BufferSize];
                // OPEN_REPARSE_POINT alone still allows ProjFS to return projected-only names.
                uint queryFlags = ReturnOnDiskEntriesOnly | RestartScan;
                while (true)
                {
                    int status = NtQueryDirectoryFileEx(
                        directory, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out IoStatusBlock ioStatus,
                        buffer, (uint)buffer.Length, FileIdExtdDirectoryInformation, queryFlags, IntPtr.Zero);
                    queryFlags = ReturnOnDiskEntriesOnly;
                    if (status == StatusNoMoreFiles)
                    {
                        yield break;
                    }

                    if (status < 0)
                    {
                        throw StatusException(status, "Failed to enumerate physical backup entries.");
                    }

                    int length = checked((int)ioStatus.Information.ToUInt64());
                    int offset = 0;
                    while (true)
                    {
                        Entry entry = ReadEntry(buffer, offset, length, out int nextOffset);
                        if (entry.Name != "." && entry.Name != "..")
                        {
                            yield return entry;
                        }

                        if (nextOffset == 0)
                        {
                            break;
                        }

                        offset += nextOffset;
                    }
                }
            }

            public static void MarkForDeletion(SafeFileHandle handle)
            {
                // Remove the name when our handle closes, even if a delete-sharing reader remains open.
                // Do not change attributes through a path or recall file data to clear read-only state.
                uint flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadonly;
                if (!SetFileInformationByHandle(handle, FileDispositionInfoEx, ref flags, sizeof(uint)))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to delete a physical backup entry.");
                }
            }

            private static AttributeTag GetAttributes(SafeFileHandle handle)
            {
                if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out AttributeTag attributes, (uint)Marshal.SizeOf<AttributeTag>()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to read backup entry attributes.");
                }

                return attributes;
            }

            private static Entry ReadEntry(byte[] buffer, int offset, int length, out int nextOffset)
            {
                if (length > buffer.Length || offset < 0 || offset > length - DirectoryEntryHeaderSize)
                {
                    throw new IOException("The physical directory query returned an invalid record.");
                }

                nextOffset = BitConverter.ToInt32(buffer, offset);
                int nameLength = BitConverter.ToInt32(buffer, offset + 60);
                if (nameLength < 0 || (nameLength % sizeof(char)) != 0 ||
                    nameLength > length - offset - DirectoryEntryHeaderSize ||
                    (nextOffset != 0 && (nextOffset < DirectoryEntryHeaderSize + nameLength || nextOffset > length - offset)))
                {
                    throw new IOException("The physical directory query returned an invalid entry length.");
                }

                // NTFS names preserve raw UTF-16 code units, including unpaired surrogates.
                return new Entry(
                    new string(MemoryMarshal.Cast<byte, char>(buffer.AsSpan(offset + DirectoryEntryHeaderSize, nameLength))),
                    (BitConverter.ToUInt32(buffer, offset + 56) & DirectoryAttribute) != 0,
                    new Guid(buffer.AsSpan(offset + 72, 16)));
            }

            private static Win32Exception StatusException(int status, string message) =>
                new Win32Exception(unchecked((int)RtlNtStatusToDosError(status)), message);

            public readonly struct Entry
            {
                public Entry(string name, bool isDirectory, Guid fileId)
                {
                    this.Name = name;
                    this.IsDirectory = isDirectory;
                    this.FileId = fileId;
                }

                public string Name { get; }
                public bool IsDirectory { get; }
                public Guid FileId { get; }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct FileIdentity
            {
                public ulong Volume;
                public Guid FileId;

                public override string ToString() => $"{this.Volume:X16}:{this.FileId:N}";
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct AttributeTag
            {
                public uint Attributes;
                public uint Tag;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct UnicodeString
            {
                public ushort Length;
                public ushort MaximumLength;
                public IntPtr Buffer;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct ObjectAttributes
            {
                public int Length;
                public IntPtr RootDirectory;
                public IntPtr ObjectName;
                public uint Attributes;
                public IntPtr SecurityDescriptor;
                public IntPtr SecurityQualityOfService;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IoStatusBlock
            {
                public IntPtr Status;
                public UIntPtr Information;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern SafeFileHandle CreateFileW(
                string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

            [DllImport("ntdll.dll")]
            private static extern int NtCreateFile(
                out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock ioStatus,
                IntPtr allocationSize, uint fileAttributes, FileShare share, uint disposition, uint options,
                IntPtr extendedAttributes, uint extendedAttributesLength);

            [DllImport("ntdll.dll")]
            private static extern int NtQueryDirectoryFileEx(
                SafeFileHandle handle, IntPtr eventHandle, IntPtr apcRoutine, IntPtr apcContext,
                out IoStatusBlock ioStatus, [Out] byte[] buffer, uint length, int informationClass, uint flags, IntPtr pattern);

            [DllImport("ntdll.dll")]
            private static extern uint RtlNtStatusToDosError(int status);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetFileInformationByHandleEx(
                SafeFileHandle handle, int informationClass, out FileIdentity identity, uint size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetFileInformationByHandleEx(
                SafeFileHandle handle, int informationClass, out AttributeTag attributes, uint size);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref uint flags, uint size);
        }
    }
}
