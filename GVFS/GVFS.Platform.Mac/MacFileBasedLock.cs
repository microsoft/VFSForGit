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
        private const int InvalidFileDescriptor = -1;

        private const int LockSh = 1; // #define LOCK_SH   1    /* shared lock */
        private const int LockEx = 2; // #define LOCK_EX   2    /* exclusive lock */
        private const int LockNb = 4; // #define LOCK_NB   4    /* don't block when locking */
        private const int LockUn = 8; // #define LOCK_UN   8    /* unlock */

        int lockFileDescriptor;
        bool lockAcquired;

        public MacFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath,
            string signature)
            : base (fileSystem, tracer, lockPath, signature) 
        {
            this.lockFileDescriptor = InvalidFileDescriptor;
        }

        public override bool TryAcquireLock()
        {
            if (this.lockAcquired)
            {
                return true;
            }

            if (this.lockFileDescriptor == InvalidFileDescriptor)
            {
                this.lockFileDescriptor = Creat(this.LockPath, Convert.ToInt32("644", 8));
                if (this.lockFileDescriptor == InvalidFileDescriptor)
                {
                    int errno = Marshal.GetLastWin32Error();

                    this.Tracer.RelatedWarning($"Failed to create lock file descriptor for '{this.LockPath}': {errno}"); 

                    return false;
                }
            }

            if (Flock(this.lockFileDescriptor, LockEx | LockNb) != 0)
            {
                int errno = Marshal.GetLastWin32Error();

                // Log error if not EWOULDBLOCK
                this.Tracer.RelatedInfo($"Failed to acquire lock for '{this.LockPath}': {errno}");

                return false;
            }

            byte[] signatureBytes = Encoding.UTF8.GetBytes(this.Signature);
            long bytesWritten = Write(
                this.lockFileDescriptor,
                signatureBytes,
                Convert.ToUInt64(signatureBytes.Length));
            
            if (bytesWritten == -1)
            {
                int errno = Marshal.GetLastWin32Error();

                this.Tracer.RelatedWarning($"Failed to write signature for '{this.LockPath}': {errno}");
            }

            this.Tracer.RelatedInfo($"Lock acquired for for '{this.LockPath}'");

            return true;
        }

        public override void Dispose()
        {
            if (this.lockAcquired)
            {
                if (Flock(this.lockFileDescriptor, LockUn) != 0)
                {
                    this.Tracer.RelatedWarning($"Failed to release lock for: '{this.LockPath}'");
                }

                this.Tracer.RelatedInfo($"Lock released: '{this.LockPath}'");

                this.lockAcquired = false;
            }

            if (this.lockFileDescriptor != InvalidFileDescriptor)
            {
                if (Close(this.lockFileDescriptor) != 0)
                {
                    int errno = Marshal.GetLastWin32Error();

                    this.Tracer.RelatedWarning($"Failed to close fd for lock: '{this.LockPath}'");
                }

                this.lockFileDescriptor = InvalidFileDescriptor;
            }
        }

        [DllImport("libc", EntryPoint = "creat", SetLastError = true)]
        private static extern int Creat(string pathname, int mode);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static extern long Write(int fd, byte[] buf, ulong count);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        private static extern int Flock(int fd, int operation);
    }
}
