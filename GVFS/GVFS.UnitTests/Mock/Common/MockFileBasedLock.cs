using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockFileBasedLock : FileBasedLock
    {
        public MockFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
        }

        public override bool TryAcquireLock(out Exception lockException)
        {
            lockException = null;
            return true;
        }

        public override void Dispose()
        {
        }
    }
}
