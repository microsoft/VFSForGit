using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Virtualization;

namespace GVFS.PerfProfiling
{
    internal class ProfilingEnvironment
    {
        private GVFSDatabase gvfsDatabase;

        public ProfilingEnvironment(string enlistmentRootPath)
        {
            this.Enlistment = this.CreateEnlistment(enlistmentRootPath);
            this.Context = this.CreateContext();
            this.FileSystemCallbacks = this.CreateFileSystemCallbacks();
        }

        public GVFSEnlistment Enlistment { get; private set; }
        public GVFSContext Context { get; private set; }
        public FileSystemCallbacks FileSystemCallbacks { get; private set; }

        private GVFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            return GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, authentication: null);
        }

        private GVFSContext CreateContext()
        {
            ITracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "GVFS.PerfProfiling", disableTelemetry: true);

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo gitRepo = new GitRepo(
                tracer, 
                this.Enlistment, 
                fileSystem);
            return new GVFSContext(tracer, fileSystem, gitRepo, this.Enlistment);
        }

        private FileSystemCallbacks CreateFileSystemCallbacks()
        {
            string error;
            if (!RepoMetadata.TryInitialize(this.Context.Tracer, this.Enlistment.DotGVFSRoot, out error))
            {
                throw new InvalidRepoException(error);
            }

            string gitObjectsRoot;
            if (!RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine git objects root from repo metadata: " + error);
            }

            string localCacheRoot;
            if (!RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine local cache path from repo metadata: " + error);
            }

            string blobSizesRoot;
            if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out blobSizesRoot, out error))
            {
                throw new InvalidRepoException("Failed to determine blob sizes root from repo metadata: " + error);
            }

            this.Enlistment.InitializeCachePaths(localCacheRoot, gitObjectsRoot, blobSizesRoot);

            CacheServerInfo cacheServer = new CacheServerInfo(this.Context.Enlistment.RepoUrl, "None");
            GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(
                this.Context.Tracer, 
                this.Context.Enlistment,
                cacheServer,
                new RetryConfig());

            this.gvfsDatabase = new GVFSDatabase(this.Context.FileSystem, this.Context.Enlistment.EnlistmentRoot, new SqliteDatabase());
            GVFSGitObjects gitObjects = new GVFSGitObjects(this.Context, objectRequestor);
            return new FileSystemCallbacks(
                this.Context,
                gitObjects,
                RepoMetadata.Instance,
                blobSizes: null,
                gitIndexProjection: null,
                backgroundFileSystemTaskRunner: null,
                fileSystemVirtualizer: null,
                placeholderDatabase: new PlaceholderTable(this.gvfsDatabase),
                sparseCollection: new SparseTable(this.gvfsDatabase),
                gitStatusCache : null);
        }
    }
}
