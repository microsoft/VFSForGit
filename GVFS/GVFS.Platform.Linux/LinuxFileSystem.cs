using GVFS.Common;
using GVFS.Platform.POSIX;
using System;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Linux
{
    public class LinuxFileSystem : POSIXFileSystem
    {
        public override void ChangeMode(string path, ushort mode)
        {
            Chmod(path, mode);
        }

        public override bool HydrateFile(string fileName, byte[] buffer)
        {
            return NativeFileReader.TryReadFirstByteOfFile(fileName, buffer);
        }

        public override bool IsExecutable(string fileName)
        {
            NativeStat.StatBuffer statBuffer = this.StatFile(fileName);
            return NativeStat.IsExecutable(statBuffer.Mode);
        }

        public override bool IsSocket(string fileName)
        {
            NativeStat.StatBuffer statBuffer = this.StatFile(fileName);
            return NativeStat.IsSock(statBuffer.Mode);
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, uint mode);

        private NativeStat.StatBuffer StatFile(string fileName)
        {
            if (NativeStat.Stat(fileName, out NativeStat.StatBuffer statBuffer) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to stat {fileName}");
            }

            return statBuffer;
        }

        private static class NativeStat
        {
            // #define  S_IFMT      0170000     /* [XSI] type of file mask */
            private static readonly uint IFMT = Convert.ToUInt32("170000", 8);

            // #define  S_IFSOCK    0140000     /* [XSI] socket */
            private static readonly uint IFSOCK = Convert.ToUInt32("0140000", 8);

            // #define S_IXUSR     0000100     /* [XSI] X for owner */
            private static readonly uint IXUSR = Convert.ToUInt32("100", 8);

            // #define S_IXGRP     0000010     /* [XSI] X for group */
            private static readonly uint IXGRP = Convert.ToUInt32("10", 8);

            // #define S_IXOTH     0000001     /* [XSI] X for other */
            private static readonly uint IXOTH = Convert.ToUInt32("1", 8);

            // #define _STAT_VER   1
            private static readonly int STAT_VER = 1;

            public static bool IsSock(uint mode)
            {
                // #define  S_ISSOCK(m) (((m) & S_IFMT) == S_IFSOCK)    /* socket */
                return (mode & IFMT) == IFSOCK;
            }

            public static bool IsExecutable(uint mode)
            {
                return (mode & (IXUSR | IXGRP | IXOTH)) != 0;
            }

            public static int Stat(string path, [Out] out StatBuffer buf)
            {
                return __XStat64(STAT_VER, path, out buf);
            }

            // TODO(Linux): assumes recent GNU libc or ABI-compatible libc
            [DllImport("libc", EntryPoint = "__xstat64", SetLastError = true)]
            public static extern int __XStat64(int vers, string path, [Out] out StatBuffer buf);

            [StructLayout(LayoutKind.Sequential)]
            public struct TimeSpec
            {
                public long Sec;
                public long Nsec;
            }

            // TODO(Linux): assumes stat64 field layout of x86-64 architecture
            [StructLayout(LayoutKind.Sequential)]
            public struct StatBuffer
            {
                public ulong Dev;           /* ID of device containing file */
                public ulong Ino;           /* File serial number */
                public ulong NLink;         /* Number of hard links */
                public uint Mode;           /* Mode of file (see below) */
                public uint UID;            /* User ID of the file */
                public uint GID;            /* Group ID of the file */
                public uint Padding;        /* RESERVED: DO NOT USE! */
                public ulong RDev;          /* Device ID if special file */
                public long Size;           /* file size, in bytes */
                public long BlkSize;        /* optimal blocksize for I/O */
                public long Blocks;         /* blocks allocated for file */
                public TimeSpec ATimespec;  /* time of last access */
                public TimeSpec MTimespec;  /* time of last data modification */
                public TimeSpec CTimespec;  /* time of last status change */

                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
                public long[] Reserved;     /* RESERVED: DO NOT USE! */
            }
        }

        private static class NativeFileReader
        {
            private const int ReadOnly = 0x0000;

            internal static bool TryReadFirstByteOfFile(string fileName, byte[] buffer)
            {
                int fileDescriptor = -1;
                bool readStatus = false;
                try
                {
                    fileDescriptor = Open(fileName, ReadOnly);
                    if (fileDescriptor != -1)
                    {
                        readStatus = TryReadOneByte(fileDescriptor, buffer);
                    }
                }
                finally
                {
                    Close(fileDescriptor);
                }

                return readStatus;
            }

            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            private static extern int Open(string path, int flag);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fd);

            [DllImport("libc", EntryPoint = "read", SetLastError = true)]
            private static extern long Read(int fd, [Out] byte[] buf, ulong count);

            private static bool TryReadOneByte(int fileDescriptor, byte[] buffer)
            {
                long numBytes = Read(fileDescriptor, buffer, 1);

                if (numBytes == -1)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
