using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Platform.Mac
{
    public class MacFileBasedLock : FileBasedLock
    {
        // #define O_WRONLY    0x0001      /* open for writing only */
        private const int OpenWriteOnly = 0x0001; 

        // #define O_CREAT     0x0200      /* create if nonexistant */
        private const int OpenCreate = 0x0200;    

        // #define EINTR       4       /* Interrupted system call */
        private const int EIntr = 4; 

        // #define EAGAIN      35      /* Resource temporarily unavailable */
        // #define EWOULDBLOCK EAGAIN  /* Operation would block */
        private const int EWouldBlock = 35; 

        private const int InvalidFileDescriptor = -1;

        private static readonly ushort FileMode644 = Convert.ToUInt16("644", 8);

        private int lockFileDescriptor;

        public MacFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath,
            string signature)
            : base(fileSystem, tracer, lockPath, signature) 
        {
            this.lockFileDescriptor = InvalidFileDescriptor;
        }

        [Flags]
        private enum FLockOperations
        {
            LockSh = 1, // #define LOCK_SH   1    /* shared lock */
            LockEx = 2, // #define LOCK_EX   2    /* exclusive lock */
            LockNb = 4, // #define LOCK_NB   4    /* don't block when locking */
            LockUn = 8  // #define LOCK_UN   8    /* unlock */
        }

        public override bool TryAcquireLock()
        {
            if (this.lockFileDescriptor == InvalidFileDescriptor)
            {
                this.lockFileDescriptor = NativeMethods.Open(this.LockPath, OpenCreate | OpenWriteOnly, FileMode644);
                if (this.lockFileDescriptor == InvalidFileDescriptor)
                {
                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(MacFileBasedLock)}.{nameof(this.TryAcquireLock)}: Failed to open lock file");
                    
                    return false;
                }
            }

            if (NativeMethods.FLock(this.lockFileDescriptor, (int)(FLockOperations.LockEx | FLockOperations.LockNb)) != 0)
            {
                int errno = Marshal.GetLastWin32Error();
                if (errno != EIntr && errno != EWouldBlock)
                {
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(MacFileBasedLock)}.{nameof(this.TryAcquireLock)}: Unexpected error when locking file");
                }

                return false;
            }

            if (NativeMethods.FTruncate(this.lockFileDescriptor, 0) == 0)
            {
                byte[] signatureBytes = Encoding.UTF8.GetBytes(this.Signature);
                long bytesWritten = NativeMethods.Write(
                    this.lockFileDescriptor,
                    signatureBytes,
                    Convert.ToUInt64(signatureBytes.Length));

                if (bytesWritten == -1)
                {
                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(MacFileBasedLock)}.{nameof(this.TryAcquireLock)}: Failed to write signature");
                }
            }
            else
            {
                int errno = Marshal.GetLastWin32Error();
                EventMetadata metadata = this.CreateEventMetadata(errno);
                this.Tracer.RelatedWarning(
                    metadata,
                    $"{nameof(MacFileBasedLock)}.{nameof(this.TryAcquireLock)}: Failed to truncate lock file");
            }

            return true;
        }

        public override void Dispose()
        {
            if (this.lockFileDescriptor != InvalidFileDescriptor)
            {
                if (NativeMethods.Close(this.lockFileDescriptor) != 0)
                {
                    int errno = Marshal.GetLastWin32Error();
                    EventMetadata metadata = this.CreateEventMetadata(errno);
                    this.Tracer.RelatedWarning(
                        metadata,
                        $"{nameof(MacFileBasedLock)}.{nameof(this.Dispose)}: Error when closing lock fd");
                }

                this.lockFileDescriptor = InvalidFileDescriptor;
            }
        }

        private EventMetadata CreateEventMetadata(int errno = 0)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "MacFileBasedLock");
            metadata.Add(nameof(this.LockPath), this.LockPath);
            metadata.Add(nameof(this.Signature), this.Signature);
            if (errno != 0)
            {
                metadata.Add(nameof(errno), errno);
            }

            return metadata;
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            public static extern int Open(string pathname, int flags, ushort mode);

            [DllImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
            public static extern long FTruncate(int fd, long length);

            [DllImport("libc", EntryPoint = "write", SetLastError = true)]
            public static extern long Write(int fd, byte[] buf, ulong count);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            public static extern int Close(int fd);

            [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
            public static extern int FLock(int fd, int operation);
        }
    }
}
