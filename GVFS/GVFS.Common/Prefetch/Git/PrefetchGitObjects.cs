using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;

namespace GVFS.Common.Prefetch.Git
{
    public class PrefetchGitObjects : GitObjects
    {
        public PrefetchGitObjects(ITracer tracer, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor, PhysicalFileSystem fileSystem = null) : base(tracer, enlistment, objectRequestor, fileSystem)
        {
        }
    }
}
