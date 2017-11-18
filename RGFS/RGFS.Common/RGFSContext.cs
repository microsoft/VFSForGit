using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Tracing;
using System;

namespace RGFS.Common
{
    public class RGFSContext : IDisposable
    {
        private bool disposedValue = false;
        
        public RGFSContext(ITracer tracer, PhysicalFileSystem fileSystem, GitRepo repository, RGFSEnlistment enlistment)
        {
            this.Tracer = tracer;
            this.FileSystem = fileSystem;
            this.Enlistment = enlistment;
            this.Repository = repository;

            this.Unattended = RGFSEnlistment.IsUnattended(this.Tracer);
        }

        public ITracer Tracer { get; private set; }
        public PhysicalFileSystem FileSystem { get; private set; }
        public GitRepo Repository { get; private set; }
        public RGFSEnlistment Enlistment { get; private set; }
        public bool Unattended { get; private set; }

        public void Dispose()
        {
            this.Dispose(true);
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
