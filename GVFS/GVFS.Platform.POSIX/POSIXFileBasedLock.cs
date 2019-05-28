using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.POSIX
{
    public abstract class POSIXFileBasedLock : FileBasedLock
    {
        private int lockFileDescriptor;

        public POSIXFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
            this.lockFileDescriptor = NativeMethods.InvalidFileDescriptor;
        }

        protected abstract int EIntr { get; }
        protected abstract int EWouldBlock { get; }

        public override bool TryAcquireLock()
        {
            if (this.lockFileDescriptor == NativeMethods.InvalidFileDescriptor)
            {
                this.FileSystem.CreateDirectory(Path.GetDirectoryName(this.LockPath));

                this.lockFileDescriptor = this.CreateFile(this.LockPath);

                if (this.lockFileDescriptor == NativeMethods.InvalidFileDescriptor)
                {
                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(POSIXFileBasedLock)}.{nameof(this.TryAcquireLock)}: Failed to open lock file");

                    return false;
                }
            }

            if (NativeMethods.FLock(this.lockFileDescriptor, NativeMethods.LockEx | NativeMethods.LockNb) != 0)
            {
                int errno = Marshal.GetLastWin32Error();
                if (errno != this.EIntr && errno != this.EWouldBlock)
                {
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(POSIXFileBasedLock)}.{nameof(this.TryAcquireLock)}: Unexpected error when locking file");
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
                        $"{nameof(POSIXFileBasedLock)}.{nameof(this.Dispose)}: Error when closing lock fd");
                }

                this.lockFileDescriptor = NativeMethods.InvalidFileDescriptor;
            }
        }

        protected abstract int CreateFile(string path);

        private EventMetadata CreateEventMetadata(int errno = 0)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(POSIXFileBasedLock));
            metadata.Add(nameof(this.LockPath), this.LockPath);
            if (errno != 0)
            {
                metadata.Add(nameof(errno), errno);
            }

            return metadata;
        }

        private static class NativeMethods
        {
            public const int LockSh = 1; // #define LOCK_SH   1    /* shared lock */
            public const int LockEx = 2; // #define LOCK_EX   2    /* exclusive lock */
            public const int LockNb = 4; // #define LOCK_NB   4    /* don't block when locking */
            public const int LockUn = 8; // #define LOCK_UN   8    /* unlock */

            public const int InvalidFileDescriptor = -1;

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            public static extern int Close(int fd);

            [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
            public static extern int FLock(int fd, int operation);
        }
    }
}
