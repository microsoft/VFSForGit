using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public abstract class FileBasedLock : IDisposable
    {
        public FileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            this.FileSystem = fileSystem;
            this.Tracer = tracer;
            this.LockPath = lockPath;
        }

        protected PhysicalFileSystem FileSystem { get; }
        protected string LockPath { get; }
        protected ITracer Tracer { get; }

        public bool TryAcquireLock()
        {
            return this.TryAcquireLock(out _);
        }

        /// <summary>
        /// Attempts to acquire the lock, providing the exception that prevented acquisition.
        /// </summary>
        /// <param name="lockException">
        /// When the method returns false, contains the exception that prevented lock acquisition.
        /// Callers can pattern-match on the exception type to distinguish lock contention
        /// (e.g. <see cref="System.IO.IOException"/> with a sharing violation HResult) from
        /// permission errors (<see cref="UnauthorizedAccessException"/>) or other failures.
        /// Null when the method returns true.
        /// </param>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        public abstract bool TryAcquireLock(out Exception lockException);

        public abstract void Dispose();
    }
}
