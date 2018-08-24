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
            string lockPath,
            string signature)
        {
            this.FileSystem = fileSystem;
            this.Tracer = tracer;
            this.LockPath = lockPath;
            this.Signature = signature;
        }

        protected PhysicalFileSystem FileSystem { get; }
        protected string LockPath { get; }
        protected ITracer Tracer { get; }
        protected string Signature { get; }

        public abstract bool TryAcquireLock();

        public abstract void Dispose();
    }
}
