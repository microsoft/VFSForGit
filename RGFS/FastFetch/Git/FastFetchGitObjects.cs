using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.Tracing;

namespace FastFetch.Git
{
    public class FastFetchGitObjects : GitObjects
    {
        public FastFetchGitObjects(ITracer tracer, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor, PhysicalFileSystem fileSystem = null) : base(tracer, enlistment, objectRequestor, fileSystem)
        {
        }
    }
}
