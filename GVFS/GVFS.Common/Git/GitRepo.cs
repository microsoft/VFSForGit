using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static GVFS.Common.Git.LibGit2Repo;

namespace GVFS.Common.Git
{
    public class GitRepo : IDisposable
    {
        private static readonly byte[] LooseBlobHeader = new byte[] { (byte)'b', (byte)'l', (byte)'o', (byte)'b', (byte)' ' };

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private LibGit2RepoInvoker libgit2RepoInvoker;
        private Enlistment enlistment;

        public GitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem, Func<LibGit2Repo> repoFactory = null)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.fileSystem = fileSystem;

            this.GVFSLock = new GVFSLock(tracer);

            this.libgit2RepoInvoker = new LibGit2RepoInvoker(
                tracer,
                repoFactory ?? (() => new LibGit2Repo(this.tracer, this.enlistment.WorkingDirectoryBackingRoot)));
        }

        // For Unit Testing
        protected GitRepo(ITracer tracer)
        {
            this.GVFSLock = new GVFSLock(tracer);
        }

        private enum LooseBlobState
        {
            Invalid,
            Missing,
            Exists,
            Corrupt,
            Unknown,
        }

        public GVFSLock GVFSLock
        {
            get;
            private set;
        }

        internal LibGit2RepoInvoker LibGit2RepoInvoker
        {
            get { return this.libgit2RepoInvoker; }
        }

        public void CloseActiveRepo()
        {
            this.libgit2RepoInvoker?.DisposeSharedRepo();
        }

        public void OpenRepo()
        {
            this.libgit2RepoInvoker?.InitializeSharedRepo();
        }

        public bool TryGetObjectType(string sha, out Native.ObjectTypes? objectType)
        {
            return this.libgit2RepoInvoker.TryInvoke(repo => repo.GetObjectType(sha), out objectType);
        }

        public virtual bool TryCopyBlobContentStream(string blobSha, Action<Stream, long> writeAction)
        {
            LooseBlobState state = this.GetLooseBlobState(blobSha, writeAction, out long size);

            if (state == LooseBlobState.Exists)
            {
                return true;
            }
            else if (state != LooseBlobState.Missing)
            {
                return false;
            }

            if (!this.libgit2RepoInvoker.TryInvoke(repo => repo.TryCopyBlob(blobSha, writeAction), out bool copyBlobResult))
            {
                return false;
            }

            return copyBlobResult;
        }

        public virtual bool CommitAndRootTreeExists(string commitSha, out string rootTreeSha)
        {
            bool output = false;
            string treeShaLocal = null;
            this.libgit2RepoInvoker.TryInvoke(repo => repo.CommitAndRootTreeExists(commitSha, out treeShaLocal), out output);
            rootTreeSha = treeShaLocal;
            return output;
        }

        public virtual bool ObjectExists(string blobSha)
        {
            bool output = false;
            this.libgit2RepoInvoker.TryInvoke(repo => repo.ObjectExists(blobSha), out output);
            return output;
        }

        /// <summary>
        /// Try to find the size of a given blob by SHA1 hash.
        ///
        /// Returns true iff the blob exists as a loose object.
        /// </summary>
        public virtual bool TryGetBlobLength(string blobSha, out long size)
        {
            return this.GetLooseBlobState(blobSha, null, out size) == LooseBlobState.Exists;
        }

        /// <summary>
        /// Try to find the SHAs of subtrees missing from the given tree.
        /// </summary>
        /// <param name="treeSha">Tree to look up</param>
        /// <param name="subtrees">SHAs of subtrees of this tree which are not downloaded yet.</param>
        /// <returns></returns>
        public virtual bool TryGetMissingSubTrees(string treeSha, out string[] subtrees)
        {
            string[] missingSubtrees = null;
            var succeeded = this.libgit2RepoInvoker.TryInvoke(repo =>
                repo.GetMissingSubTrees(treeSha), out missingSubtrees);
            subtrees = missingSubtrees;
            return succeeded;
        }

        public void Dispose()
        {
            if (this.libgit2RepoInvoker != null)
            {
                this.libgit2RepoInvoker.Dispose();
                this.libgit2RepoInvoker = null;
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

        private LooseBlobState GetLooseBlobStateAtPath(string blobPath, Action<Stream, long> writeAction, out long size)
        {
            bool corruptLooseObject = false;
            try
            {
                if (this.fileSystem.FileExists(blobPath))
                {
                    using (Stream file = this.fileSystem.OpenFileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read, callFlushFileBuffers: false))
                    {
                        // The DeflateStream header starts 2 bytes into the gzip header, but they are otherwise compatible
                        file.Position = 2;
                        using (DeflateStream deflate = new DeflateStream(file, CompressionMode.Decompress))
                        {
                            if (!ReadLooseObjectHeader(deflate, out size))
                            {
                                corruptLooseObject = true;
                                return LooseBlobState.Corrupt;
                            }

                            if (writeAction != null)
                            {
                                CountingStream counting = new CountingStream(deflate);
                                writeAction(counting, size);

                                // .NET 10 DeflateStream silently returns partial data on truncated
                                // zlib input instead of throwing InvalidDataException. Detect this
                                // by comparing the bytes actually read to the size in the header.
                                if (counting.BytesRead < size)
                                {
                                    corruptLooseObject = true;
                                    return LooseBlobState.Corrupt;
                                }
                            }

                            return LooseBlobState.Exists;
                        }
                    }
                }

                size = -1;
                return LooseBlobState.Missing;
            }
            catch (InvalidDataException ex)
            {
                corruptLooseObject = true;

                EventMetadata metadata = new EventMetadata();
                metadata.Add("blobPath", blobPath);
                metadata.Add("Exception", ex.ToString());
                this.tracer.RelatedWarning(metadata, nameof(this.GetLooseBlobStateAtPath) + ": Failed to stream blob (InvalidDataException)", Keywords.Telemetry);

                size = -1;
                return LooseBlobState.Corrupt;
            }
            catch (IOException ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("blobPath", blobPath);
                metadata.Add("Exception", ex.ToString());
                this.tracer.RelatedWarning(metadata, nameof(this.GetLooseBlobStateAtPath) + ": Failed to stream blob from disk", Keywords.Telemetry);

                size = -1;
                return LooseBlobState.Unknown;
            }
            finally
            {
                if (corruptLooseObject)
                {
                    string corruptBlobsFolderPath = Path.Combine(this.enlistment.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, GVFSConstants.DotGVFS.CorruptObjectsName);
                    string corruptBlobPath = Path.Combine(corruptBlobsFolderPath, Path.GetRandomFileName());

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("blobPath", blobPath);
                    metadata.Add("corruptBlobPath", corruptBlobPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GetLooseBlobStateAtPath) + ": Renaming corrupt loose object");
                    this.tracer.RelatedEvent(EventLevel.Informational, nameof(this.GetLooseBlobStateAtPath) + "_RenameCorruptObject", metadata);

                    try
                    {
                        this.fileSystem.CreateDirectory(corruptBlobsFolderPath);
                        this.fileSystem.MoveFile(blobPath, corruptBlobPath);
                    }
                    catch (Exception e)
                    {
                        metadata = new EventMetadata();
                        metadata.Add("blobPath", blobPath);
                        metadata.Add("blobBackupPath", corruptBlobPath);
                        metadata.Add("Exception", e.ToString());
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.GetLooseBlobStateAtPath) + ": Failed to rename corrupt loose object");
                        this.tracer.RelatedEvent(EventLevel.Warning, nameof(this.GetLooseBlobStateAtPath) + "_RenameCorruptObjectFailed", metadata, Keywords.Telemetry);
                    }
                }
            }
        }

        private LooseBlobState GetLooseBlobState(string blobSha, Action<Stream, long> writeAction, out long size)
        {
            // Ensure SHA path is lowercase for case-sensitive filesystems
            if (GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem)
            {
                blobSha = blobSha.ToLower();
            }

            string blobPath = Path.Combine(
                this.enlistment.GitObjectsRoot,
                blobSha.Substring(0, 2),
                blobSha.Substring(2));

            LooseBlobState state = this.GetLooseBlobStateAtPath(blobPath, writeAction, out size);
            if (state == LooseBlobState.Missing)
            {
                blobPath = Path.Combine(
                   this.enlistment.LocalObjectsRoot,
                   blobSha.Substring(0, 2),
                   blobSha.Substring(2));
                state = this.GetLooseBlobStateAtPath(blobPath, writeAction, out size);
            }

            return state;
        }

        /// <summary>
        /// A read-only stream wrapper that counts the total bytes read.
        /// Used to detect truncated loose objects where DeflateStream returns
        /// fewer bytes than the header declares (see GetLooseBlobStateAtPath).
        /// </summary>
        private sealed class CountingStream : Stream
        {
            private readonly Stream inner;
            private long bytesRead;

            public CountingStream(Stream inner)
            {
                this.inner = inner;
            }

            public long BytesRead => this.bytesRead;

            public override bool CanRead => this.inner.CanRead;
            public override bool CanSeek => this.inner.CanSeek;
            public override bool CanWrite => this.inner.CanWrite;
            public override long Length => this.inner.Length;
            public override long Position
            {
                get => this.inner.Position;
                set => this.inner.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = this.inner.Read(buffer, offset, count);
                this.bytesRead += read;
                return read;
            }

            public override int ReadByte()
            {
                int b = this.inner.ReadByte();
                if (b >= 0)
                {
                    this.bytesRead++;
                }

                return b;
            }

            public override void Flush() => this.inner.Flush();
            public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);
            public override void SetLength(long value) => this.inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);
        }
    }
}
