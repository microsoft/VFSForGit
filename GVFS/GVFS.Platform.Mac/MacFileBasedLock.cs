using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;

namespace GVFS.Platform.Mac
{
    public class MacFileBasedLock : POSIXFileBasedLock
    {
        public MacFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
        }
    }
}