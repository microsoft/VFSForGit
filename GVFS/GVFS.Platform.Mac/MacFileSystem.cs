using GVFS.Common;
using GVFS.Platform.POSIX;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Mac
{
    public class MacFileSystem : POSIXFileSystem
    {
        public static NativeStat.StatBuffer StatFile(string fileName)
        {
            if (NativeStat.Stat(fileName, out NativeStat.StatBuffer statBuffer) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to stat {fileName}");
            }

            return statBuffer;
        }

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
            NativeStat.StatBuffer statBuffer = StatFile(fileName);
            return NativeStat.IsExecutable(statBuffer.Mode);
        }

        public override bool IsSocket(string fileName)
        {
            NativeStat.StatBuffer statBuffer = StatFile(fileName);
            return NativeStat.IsSock(statBuffer.Mode);
        }

        public override bool IsFileSystemSupported(string path, out string error)
        {
            error = null;

            try
            {
                string lowerCaseFilePath = Path.Combine(path, $"casetest{Guid.NewGuid().ToString()}");
                string upperCaseFilePath = lowerCaseFilePath.ToUpper();

                File.Create(lowerCaseFilePath);
                if (File.Exists(upperCaseFilePath))
                {
                    File.Delete(lowerCaseFilePath);
                    return true;
                }

                File.Delete(lowerCaseFilePath);
                error = "VFS for Git does not support case sensitive filesystems";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Exception when performing {nameof(MacFileSystem)}.{nameof(this.IsFileSystemSupported)}: {ex.ToString()}";
                return false;
            }
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, ushort mode);

        public static class NativeStat
        {
            // #define  S_IFMT      0170000     /* [XSI] type of file mask */
            private static readonly ushort IFMT = Convert.ToUInt16("170000", 8);

            // #define  S_IFSOCK    0140000     /* [XSI] socket */
            private static readonly ushort IFSOCK = Convert.ToUInt16("0140000", 8);

            // #define S_IXUSR     0000100     /* [XSI] X for owner */
            private static readonly ushort IXUSR = Convert.ToUInt16("100", 8);

            // #define S_IXGRP     0000010     /* [XSI] X for group */
            private static readonly ushort IXGRP = Convert.ToUInt16("10", 8);

            // #define S_IXOTH     0000001     /* [XSI] X for other */
            private static readonly ushort IXOTH = Convert.ToUInt16("1", 8);

            public static bool IsSock(ushort mode)
            {
                // #define  S_ISSOCK(m) (((m) & S_IFMT) == S_IFSOCK)    /* socket */
                return (mode & IFMT) == IFSOCK;
            }

            public static bool IsExecutable(ushort mode)
            {
                return (mode & (IXUSR | IXGRP | IXOTH)) != 0;
            }

            [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true)]
            public static extern int Stat(string path, [Out] out StatBuffer statBuffer);

            [StructLayout(LayoutKind.Sequential)]
            public struct TimeSpec
            {
                public long Sec;
                public long Nsec;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct StatBuffer
            {
                public int Dev;              /* ID of device containing file */
                public ushort Mode;          /* Mode of file (see below) */
                public ushort NLink;         /* Number of hard links */
                public ulong Ino;            /* File serial number */
                public uint UID;             /* User ID of the file */
                public uint GID;             /* Group ID of the file */
                public int RDev;             /* Device ID */

                public TimeSpec ATimespec;     /* time of last access */
                public TimeSpec MTimespec;     /* time of last data modification */
                public TimeSpec CTimespec;     /* time of last status change */
                public TimeSpec BirthTimespec; /* time of file creation(birth) */

                public long Size;          /* file size, in bytes */
                public long Blocks;        /* blocks allocated for file */
                public int BlkSize;        /* optimal blocksize for I/O */
                public uint Glags;         /* user defined flags for file */
                public uint Gen;           /* file generation number */
                public int LSpare;         /* RESERVED: DO NOT USE! */

                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
                public long[] QSpare;     /* RESERVED: DO NOT USE! */
            }
        }

        private static class NativeFileReader
        {
            // #define O_RDONLY 0x0000     /* open for reading only */
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
            private static extern int Read(int fd, [Out] byte[] buf, int count);

            private static bool TryReadOneByte(int fileDescriptor, byte[] buffer)
            {
                int numBytes = Read(fileDescriptor, buffer, 1);

                if (numBytes == -1)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
