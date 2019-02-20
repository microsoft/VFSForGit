using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Linux
{
    public class LinuxFileBasedLock : FileBasedLock
    {
        private int lockFileDescriptor;

        public LinuxFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
            this.lockFileDescriptor = NativeMethods.InvalidFileDescriptor;
        }

        public override bool TryAcquireLock()
        {
            if (this.lockFileDescriptor == NativeMethods.InvalidFileDescriptor)
            {
                this.FileSystem.CreateDirectory(Path.GetDirectoryName(this.LockPath));

                this.lockFileDescriptor = NativeMethods.Open(
                    this.LockPath,
                    NativeMethods.OpenCreate | NativeMethods.OpenWriteOnly,
                    NativeMethods.FileMode644);

                if (this.lockFileDescriptor == NativeMethods.InvalidFileDescriptor)
                {
                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(LinuxFileBasedLock)}.{nameof(this.TryAcquireLock)}: Failed to open lock file");

                    return false;
                }
            }

            if (NativeMethods.FLock(this.lockFileDescriptor, NativeMethods.LockEx | NativeMethods.LockNb) != 0)
            {
                int errno = Marshal.GetLastWin32Error();
                if (errno != NativeMethods.EIntr && errno != NativeMethods.EWouldBlock)
                {
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(LinuxFileBasedLock)}.{nameof(this.TryAcquireLock)}: Unexpected error when locking file");
                }

                return false;
            }

            return true;
        }

        public override void Dispose()
        {
            if (this.lockFileDescriptor != NativeMethods.InvalidFileDescriptor)
            {
                if (NativeMethods.Close(this.lockFileDescriptor) != 0)
                {
                    // Failures of close() are logged for diagnostic purposes only.
                    // It's possible that errors from a previous operation (e.g. write(2))
                    // are only reported in close().  We should *not* retry the close() if
                    // it fails since it may cause a re-used file descriptor from another
                    // thread to be closed.

                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(LinuxFileBasedLock)}.{nameof(this.Dispose)}: Error when closing lock fd");
                }

                this.lockFileDescriptor = NativeMethods.InvalidFileDescriptor;
            }
        }

        private EventMetadata CreateEventMetadata(int errno = 0)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(LinuxFileBasedLock));
            metadata.Add(nameof(this.LockPath), this.LockPath);
            if (errno != 0)
            {
                metadata.Add(nameof(errno), errno);
            }

            return metadata;
        }

        private static class NativeMethods
        {
            // #define O_WRONLY    0x0001      /* open for writing only */
            public const int OpenWriteOnly = 0x0001;

            // #define O_CREAT     0x0040      /* create if nonexistant */
            public const int OpenCreate = 0x0040;

            // #define EINTR       4       /* Interrupted system call */
            public const int EIntr = 4;

            // #define EAGAIN      11      /* Resource temporarily unavailable */
            // #define EWOULDBLOCK EAGAIN  /* Operation would block */
            public const int EWouldBlock = 11;

            public const int LockSh = 1; // #define LOCK_SH   1    /* shared lock */
            public const int LockEx = 2; // #define LOCK_EX   2    /* exclusive lock */
            public const int LockNb = 4; // #define LOCK_NB   4    /* don't block when locking */
            public const int LockUn = 8; // #define LOCK_UN   8    /* unlock */

            public const int InvalidFileDescriptor = -1;

            public static readonly uint FileMode644 = Convert.ToUInt32("644", 8);

            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            public static extern int Open(string pathname, int flags, uint mode);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            public static extern int Close(int fd);

            [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
            public static extern int FLock(int fd, int operation);
        }
    }
}
