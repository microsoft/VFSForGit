using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.Tracing;
using RGFS.GVFlt;

namespace RGFS.PerfProfiling
{
    class ProfilingEnvironment
    {
        public ProfilingEnvironment(string enlistmentRootPath)
        {
            this.Enlistment = this.CreateEnlistment(enlistmentRootPath);
            this.Context = this.CreateContext();
            this.GVFltCallbacks = this.CreateGVFltCallbacks();
        }

        public RGFSEnlistment Enlistment { get; private set; }
        public RGFSContext Context { get; private set; }
        public GVFltCallbacks GVFltCallbacks { get; private set; }

        private RGFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            string hooksPath = ProcessHelper.WhereDirectory(RGFSConstants.RGFSHooksExecutableName);

            return RGFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, hooksPath);
        }

        private RGFSContext CreateContext()
        {
            ITracer tracer = new JsonEtwTracer(RGFSConstants.RGFSEtwProviderName, "RGFS.PerfProfiling", useCriticalTelemetryFlag: false);

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(
                tracer, 
                this.Enlistment, 
                fileSystem);
            return new RGFSContext(tracer, fileSystem, gitRepo, this.Enlistment);
        }

        private GVFltCallbacks CreateGVFltCallbacks()
        {
            string error;
            if (!RepoMetadata.TryInitialize(this.Context.Tracer, this.Enlistment.DotRGFSRoot, out error))
            {
                throw new InvalidRepoException(error);
            }

            CacheServerInfo cacheServer = new CacheServerInfo(this.Context.Enlistment.RepoUrl, "None");
            GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(
                this.Context.Tracer, 
                this.Context.Enlistment,
                cacheServer,
                new RetryConfig());

            RGFSGitObjects gitObjects = new RGFSGitObjects(this.Context, objectRequestor);
            return new GVFltCallbacks(this.Context, gitObjects, RepoMetadata.Instance);
        }
    }
}
