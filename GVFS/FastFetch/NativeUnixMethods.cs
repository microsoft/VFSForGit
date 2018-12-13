using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace FastFetch
{
    public class NativeUnixMethods 
    {
        public const int ReadOnly = 0x0000;
        public const int WriteOnly = 0x0001;

        public const int Create = 0x0200;
        public const int Truncate = 0x0400;
        private const int InvalidFileDescriptor = -1;

        public static unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination, ushort mode)
        {
            int fileDescriptor = InvalidFileDescriptor;
            try
            {
                fileDescriptor = Open(destination, WriteOnly | Create | Truncate, mode);
                if (fileDescriptor != InvalidFileDescriptor)
                {
                    IntPtr result = Write(fileDescriptor, originalData, (IntPtr)originalSize);
                    if (result.ToInt32() == -1)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
            }
            catch (Win32Exception e)
            {
                tracer.RelatedError("Error writing file {0}. Win32Exception: {1}", destination, e);
                throw;
            }
            finally
            {
                Close(fileDescriptor);
            }
        }

        public static bool StatAndUpdateIndexForFile(string path, MemoryMappedViewAccessor indexView, long offset)
        {
            NativeStat.StatBuffer st = StatFile(path);
            Index.IndexEntry indexEntry = new Index.IndexEntry(indexView, offset);
            indexEntry.MtimeSeconds = (uint)st.MTimespec.Sec;
            indexEntry.MtimeNanosecondFraction = (uint)st.MTimespec.Nsec;
            indexEntry.CtimeSeconds = (uint)st.CTimespec.Sec;
            indexEntry.CtimeNanosecondFraction = (uint)st.CTimespec.Nsec;
            indexEntry.Size = (uint)st.Size;
            indexEntry.Dev = (uint)st.Dev;
            indexEntry.Ino = (uint)st.Ino;
            indexEntry.Uid = st.UID;
            indexEntry.Gid = st.GID;
            return true;
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string path, int flag, ushort creationMode);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static unsafe extern IntPtr Write(int fileDescriptor, void* buf, IntPtr count);

        private static NativeStat.StatBuffer StatFile(string fileName)
        {
            NativeStat.StatBuffer statBuffer = new NativeStat.StatBuffer();
            if (NativeStat.Stat(fileName, out statBuffer) != 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to stat {fileName}");
            }

            return statBuffer;
        }

        private static class NativeStat
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
    }
}
