using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Platform.Mac
{
    public class MacFileBasedLock : FileBasedLock
    {
        public MacFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath,
            string signature)
            : base (fileSystem, tracer, lockPath, signature) 
        {
        }

        public override bool TryAcquireLock()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
        }
    }
}
