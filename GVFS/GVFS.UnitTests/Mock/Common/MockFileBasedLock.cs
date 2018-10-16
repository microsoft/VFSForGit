using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;

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

        public override bool TryAcquireLock()
        {
            return true;
        }

        public override void Dispose()
        {
        }
    }
}
