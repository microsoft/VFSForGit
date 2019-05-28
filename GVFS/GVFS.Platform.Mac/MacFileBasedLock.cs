using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Mac
{
    public class MacFileBasedLock : POSIXFileBasedLock
    {
        public MacFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
        }

        protected override int EIntr => NativeMethods.EIntr;
        protected override int EWouldBlock => NativeMethods.EWouldBlock;

        protected override int CreateFile(string path) =>
            NativeMethods.Open(
                path,
                NativeMethods.OpenCreate | NativeMethods.OpenWriteOnly,
                NativeMethods.FileMode644);

        private static class NativeMethods
        {
            // #define O_WRONLY    0x0001      /* open for writing only */
            public const int OpenWriteOnly = 0x0001;

            // #define O_CREAT     0x0200      /* create if nonexistant */
            public const int OpenCreate = 0x0200;

            // #define EINTR       4       /* Interrupted system call */
            public const int EIntr = 4;

            // #define EAGAIN      35      /* Resource temporarily unavailable */
            // #define EWOULDBLOCK EAGAIN  /* Operation would block */
            public const int EWouldBlock = 35;

            public static readonly ushort FileMode644 = Convert.ToUInt16("644", 8);

            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            public static extern int Open(string path, int flags, ushort mode);
        }
    }
}
