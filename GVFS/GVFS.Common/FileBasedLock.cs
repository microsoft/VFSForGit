using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public abstract class FileBasedLock : IDisposable
    {
        protected readonly PhysicalFileSystem FileSystem;
        protected readonly string LockPath;
        protected readonly ITracer Tracer;
        protected readonly string Signature;

        public FileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath,
            string signature)
        {
            this.FileSystem = fileSystem;
            this.Tracer = tracer;
            this.LockPath = lockPath;
            this.Signature = signature;
        }

        public abstract bool TryAcquireLock();

        public abstract void Dispose();
    }
}
