using GVFS.Common.Git;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Physical.Git
{
    public class GitRepo : IDisposable
    {
        private string workingDirectoryPath;
        private ITracer tracer;

        private PhysicalFileSystem fileSystem;
        
        private GitIndex index;
        private ProcessPool<GitCatFileBatchProcess> catFileProcessPool;
        private ProcessPool<GitCatFileBatchCheckProcess> batchCheckProcessPool;

        public GitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem, GitIndex index)
        {
            this.tracer = tracer;
            this.workingDirectoryPath = enlistment.WorkingDirectoryRoot;
            this.fileSystem = fileSystem;
            this.index = index;

            this.GVFSLock = new GVFSLock(tracer);

            this.batchCheckProcessPool = new ProcessPool<GitCatFileBatchCheckProcess>(
                tracer,
                () => new GitCatFileBatchCheckProcess(enlistment),
                Environment.ProcessorCount);
            this.catFileProcessPool = new ProcessPool<GitCatFileBatchProcess>(
                tracer,
                () => new GitCatFileBatchProcess(enlistment),
                Environment.ProcessorCount);
        }

        public GitIndex Index
        {
            get { return this.index; }
        }

        public GVFSLock GVFSLock
        {
            get;
            private set;
        }

        public void Initialize()
        {
            this.Index.Initialize();
        }

        public virtual string GetHeadTreeSha()
        {
            return this.catFileProcessPool.Invoke(
                catFile => catFile.GetTreeSha(GVFSConstants.HeadCommitName));
        }

        public virtual string GetHeadCommitId()
        {
            return this.catFileProcessPool.Invoke(
                catFile => catFile.GetCommitId(GVFSConstants.HeadCommitName));
        }

        public virtual bool TryCopyBlobContentStream(string blobSha, Action<StreamReader, long> writeAction)
        {
            return this.catFileProcessPool.Invoke(
                catFile => catFile.TryCopyBlobContentStream(blobSha, writeAction));
        }

        public virtual bool TryGetBlobLength(string blobSha, out long size)
        {
            long? output = this.batchCheckProcessPool.Invoke<long?>(
                catFileBatch =>
                {
                    long value;
                    if (catFileBatch.TryGetObjectSize(blobSha, out value))
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

        public virtual bool TryGetFileSha(string commitId, string virtualPath, out string sha)
        {
            sha = this.catFileProcessPool.Invoke(
               catFile =>
               {
                   string innerSha;
                   if (catFile.TryGetFileSha(commitId, virtualPath, out innerSha))
                   {
                       return innerSha;
                   }

                   return null;
               });

            return !string.IsNullOrWhiteSpace(sha);
        }

        public virtual IEnumerable<GitTreeEntry> GetTreeEntries(string commitId, string path)
        {
            return this.catFileProcessPool.Invoke(catFile => catFile.GetTreeEntries(commitId, path));
        }

        public virtual IEnumerable<GitTreeEntry> GetTreeEntries(string sha)
        {
            return this.catFileProcessPool.Invoke(catFile => catFile.GetTreeEntries(sha));
        }

        public void Dispose()
        {
            if (this.catFileProcessPool != null)
            {
                this.catFileProcessPool.Dispose();
            }

            if (this.batchCheckProcessPool != null)
            {
                this.batchCheckProcessPool.Dispose();
            }

            if (this.index != null)
            {
                this.index.Dispose();
            }
        }
    }
}
