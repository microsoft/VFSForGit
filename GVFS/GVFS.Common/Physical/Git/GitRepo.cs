using GVFS.Common.Git;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common.Physical.Git
{
    public class GitRepo : IDisposable
    {
        private string workingDirectoryPath;
        private ITracer tracer;

        private PhysicalFileSystem fileSystem;
        
        private ProcessPool<GitCatFileBatchProcess> catFileProcessPool;
        private ProcessPool<GitCatFileBatchCheckProcess> batchCheckProcessPool;

        public GitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem)
        {
            this.tracer = tracer;
            this.workingDirectoryPath = enlistment.WorkingDirectoryRoot;
            this.fileSystem = fileSystem;

            this.GVFSLock = new GVFSLock(tracer);

            this.batchCheckProcessPool = new ProcessPool<GitCatFileBatchCheckProcess>(
                tracer,
                () => new GitCatFileBatchCheckProcess(tracer, enlistment),
                Environment.ProcessorCount);
            this.catFileProcessPool = new ProcessPool<GitCatFileBatchProcess>(
                tracer,
                () => new GitCatFileBatchProcess(tracer, enlistment),
                Environment.ProcessorCount);
        }

        public GVFSLock GVFSLock
        {
            get;
            private set;
        }

        public virtual bool TryCopyBlobContentStream_CanTimeout(string blobSha, Action<StreamReader, long> writeAction)
        {
            return this.catFileProcessPool.Invoke(
                catFile => catFile.TryCopyBlobContentStream_CanTimeout(blobSha, writeAction));
        }

        public virtual bool TryGetBlobLength_CanTimeout(string blobSha, out long size)
        {
            long? output = this.batchCheckProcessPool.Invoke<long?>(
                catFileBatch =>
                {
                    long value;
                    if (catFileBatch.TryGetObjectSize_CanTimeout(blobSha, out value))
                    {
                        return value;
                    }

                    return null;
                });

            if (output.HasValue)
            {
                size = output.Value;
                return true;
            }

            size = 0;
            return false;
        }

        public void Dispose()
        {
            if (this.catFileProcessPool != null)
            {
                this.catFileProcessPool.Dispose();
                this.catFileProcessPool = null;
            }

            if (this.batchCheckProcessPool != null)
            {
                this.batchCheckProcessPool.Dispose();
                this.batchCheckProcessPool = null;
            }

            if (this.GVFSLock != null)
            {
                this.GVFSLock.Dispose();
                this.GVFSLock = null;
            }
        }
    }
}
