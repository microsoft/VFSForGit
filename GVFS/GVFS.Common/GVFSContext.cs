using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public class GVFSContext : IDisposable
    {
        private bool disposedValue = false;

        public GVFSContext(ITracer tracer, PhysicalFileSystem fileSystem, GitRepo repository, GVFSEnlistment enlistment)
        {
            this.Tracer = tracer;
            this.FileSystem = fileSystem;
            this.Enlistment = enlistment;
            this.Repository = repository;

            this.Unattended = GVFSEnlistment.IsUnattended(this.Tracer);
        }

        public ITracer Tracer { get; private set; }
        public PhysicalFileSystem FileSystem { get; private set; }
        public GitRepo Repository { get; private set; }
        public GVFSEnlistment Enlistment { get; private set; }
        public bool Unattended { get; private set; }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.Repository.Dispose();
                    this.Tracer.Dispose();
                    this.Tracer = null;
                }

                this.disposedValue = true;
            }
        }
    }
}
