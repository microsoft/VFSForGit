using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;

namespace FastFetch.Git
{
    public class FastFetchGitObjects : GitObjects
    {
        public FastFetchGitObjects(ITracer tracer, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor, PhysicalFileSystem fileSystem = null) : base(tracer, enlistment, objectRequestor, fileSystem)
        {
        }
    }
}
