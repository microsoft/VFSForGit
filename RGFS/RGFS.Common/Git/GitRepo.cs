using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RGFS.Common.Git
{
    public class GitRepo : IDisposable
    {
        private static readonly byte[] LooseBlobHeader = new byte[] { (byte)'b', (byte)'l', (byte)'o', (byte)'b', (byte)' ' };

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private LibGit2RepoPool libgit2RepoPool;
        private Enlistment enlistment;

        public GitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem, Func<LibGit2Repo> repoFactory = null)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.fileSystem = fileSystem;

            this.RGFSLock = new RGFSLock(tracer);
            
            this.libgit2RepoPool = new LibGit2RepoPool(
                tracer,
                repoFactory ?? (() => new LibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryRoot)),
                Environment.ProcessorCount * 2);
        }

        // For Unit Testing
        protected GitRepo(ITracer tracer)
        {
            this.RGFSLock = new RGFSLock(tracer);
        }

        public RGFSLock RGFSLock
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

            bool corruptLooseObject = false;
            try
            {
                if (this.fileSystem.FileExists(blobPath))
                {
                    using (Stream file = this.fileSystem.OpenFileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // The DeflateStream header starts 2 bytes into the gzip header, but they are otherwise compatible
                        file.Position = 2;
                        using (DeflateStream deflate = new DeflateStream(file, CompressionMode.Decompress))
                        {
                            long size;
                            if (!ReadLooseObjectHeader(deflate, out size))
                            {
                                corruptLooseObject = true;
                                return false;
                            }

                            writeAction(deflate, size);
                            return true;
                        }
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                corruptLooseObject = true;

                EventMetadata metadata = new EventMetadata();
                metadata.Add("blobPath", blobPath);
                metadata.Add("Exception", ex.ToString());
                this.tracer.RelatedWarning(metadata, "TryCopyBlobContentStream: Failed to stream blob (InvalidDataException)", Keywords.Telemetry);

                return false;
            }
            catch (IOException ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("blobPath", blobPath);
                metadata.Add("Exception", ex.ToString());
                this.tracer.RelatedWarning(metadata, "TryCopyBlobContentStream: Failed to stream blob from disk", Keywords.Telemetry);

                return false;
            }
            finally
            {
                if (corruptLooseObject)
                {
                    string corruptBlobsFolderPath = Path.Combine(this.enlistment.EnlistmentRoot, RGFSConstants.DotRGFS.CorruptObjectsPath);
                    string corruptBlobPath = Path.Combine(corruptBlobsFolderPath, Path.GetRandomFileName());

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("blobPath", blobPath);
                    metadata.Add("corruptBlobPath", corruptBlobPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "TryCopyBlobContentStream: Renaming corrupt loose object");
                    this.tracer.RelatedEvent(EventLevel.Informational, "TryCopyBlobContentStream_RenameCorruptObject", metadata);

                    try
                    {
                        this.fileSystem.CreateDirectory(corruptBlobsFolderPath);
                        File.Move(blobPath, corruptBlobPath);
                    }
                    catch (Exception e)
                    {
                        metadata = new EventMetadata();
                        metadata.Add("blobPath", blobPath);
                        metadata.Add("blobBackupPath", corruptBlobPath);
                        metadata.Add("Exception", e.ToString());
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, "TryCopyBlobContentStream: Failed to rename corrupt loose object");
                        this.tracer.RelatedEvent(EventLevel.Warning, "TryCopyBlobContentStream_RenameCorruptObjectFailed", metadata, Keywords.Telemetry);
                    }
                }
            }

            bool copyBlobResult;
            if (!this.libgit2RepoPool.TryInvoke(repo => repo.TryCopyBlob(blobSha, writeAction), out copyBlobResult))
            {
                return false;
            }

            return copyBlobResult;
        }

        public virtual bool CommitAndRootTreeExists(string commitSha)
        {
            bool output = false;
            this.libgit2RepoPool.TryInvoke(repo => repo.CommitAndRootTreeExists(commitSha), out output);
            return output;
        }

        public virtual bool ObjectExists(string blobSha)
        {
            bool output = false;
            this.libgit2RepoPool.TryInvoke(repo => repo.ObjectExists(blobSha), out output);
            return output;
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

            if (this.RGFSLock != null)
            {
                this.RGFSLock.Dispose();
                this.RGFSLock = null;
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
