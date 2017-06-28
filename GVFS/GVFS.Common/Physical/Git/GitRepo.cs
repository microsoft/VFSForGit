using GVFS.Common.Git;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GVFS.Common.Physical.Git
{
    public class GitRepo : IDisposable
    {
        private static readonly byte[] LooseBlobHeader = new byte[] { (byte)'b', (byte)'l', (byte)'o', (byte)'b', (byte)' ' };

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private LibGit2RepoPool libgit2RepoPool;
        private Enlistment enlistment;

        public GitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.fileSystem = fileSystem;

            this.GVFSLock = new GVFSLock(tracer);
            
            this.libgit2RepoPool = new LibGit2RepoPool(
                tracer,
                () => new LibGit2Repo(tracer, enlistment.WorkingDirectoryRoot),
                Environment.ProcessorCount * 2);
        }

        // For Unit Testing
        protected GitRepo()
        {
        }

        public GVFSLock GVFSLock
        {
            get;
            private set;
        }

        public virtual bool TryCopyBlobContentStream(string blobSha, Action<Stream, long> writeAction)
        {
            string blobPath = Path.Combine(
                this.enlistment.GitObjectsRoot, 
                blobSha.Substring(0, 2), 
                blobSha.Substring(2));
            
            try
            {
                if (File.Exists(blobPath))
                {
                    using (Stream file = new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // The DeflateStream header starts 2 bytes into the gzip header, but they are otherwise compatible
                        file.Position = 2;
                        using (DeflateStream deflate = new DeflateStream(file, CompressionMode.Decompress))
                        {
                            long size;
                            if (!ReadLooseObjectHeader(deflate, out size))
                            {
                                return false;
                            }

                            writeAction(deflate, size);
                            return true;
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                this.tracer.RelatedError("Failed to stream blob from disk: " + ex.ToString());
                return false;
            }

            bool copyBlobResult;
            if (!this.libgit2RepoPool.TryInvoke(repo => repo.TryCopyBlob(blobSha, writeAction), out copyBlobResult))
            {
                return false;
            }

            return copyBlobResult;
        }

        public virtual bool TryGetBlobLength(string blobSha, out long size)
        {
            long? output;

            if (!this.libgit2RepoPool.TryInvoke(
                repo =>
                {
                    long value;
                    if (repo.TryGetObjectSize(blobSha, out value))
                    {
                        return value;
                    }

                    return null;
                },
                out output))
            {
                size = 0;
                return false;
            }

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
            if (this.libgit2RepoPool != null)
            {
                this.libgit2RepoPool.Dispose();
                this.libgit2RepoPool = null;
            }

            if (this.GVFSLock != null)
            {
                this.GVFSLock.Dispose();
                this.GVFSLock = null;
            }
        }

        private static bool ReadLooseObjectHeader(Stream input, out long size)
        {
            size = 0;

            byte[] buffer = new byte[5];
            input.Read(buffer, 0, buffer.Length);
            if (!Enumerable.SequenceEqual(buffer, LooseBlobHeader))
            {
                return false;
            }

            while (true)
            {
                int v = input.ReadByte();
                if (v == -1)
                {
                    return false;
                }

                if (v == '\0')
                {
                    break;
                }

                size = (size * 10) + (v - '0');
            }

            return true;
        }
    }
}
