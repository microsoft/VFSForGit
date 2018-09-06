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

        public abstract bool TryAcquireLock();

        public abstract void Dispose();
    }
}
