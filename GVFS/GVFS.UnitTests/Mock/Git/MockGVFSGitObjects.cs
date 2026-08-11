using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGVFSGitObjects : GVFSGitObjects
    {
        public const uint DefaultFileLength = 100;
        private GVFSContext context;

        public MockGVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor httpGitObjects)
            : base(context, httpGitObjects)
        {
            this.context = context;
        }

        public bool CancelTryCopyBlobContentStream { get; set; }
        public bool ThrowOnTryCopyBlobContentStream { get; set; }
        public bool ThrowIOExceptionDuringCopy { get; set; }
        public uint FileLength { get; set; } = DefaultFileLength;

        public override bool TryDownloadCommit(string objectSha)
        {
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TryDownloadObjects(
                new[] { objectSha },
                onSuccess: (tryCount, response) =>
                {
                    // Add the contents to the mock repo
                    ((MockGitRepo)this.Context.Repository).AddBlob(objectSha, "DownloadedFile", response.RetryableReadToEnd());

                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                },
                onFailure: null,
                preferBatchedLooseObjects: false);

            return result.Succeeded && result.Result.Success;
        }

        public override bool TryCopyBlobContentStream(
            string sha,
            CancellationToken cancellationToken,
            RequestSource requestSource,
            Action<Stream, long> writeAction,
            out GVFSGitObjects.BlobHydrationFailureCategory failureCategory)
        {
            failureCategory = GVFSGitObjects.BlobHydrationFailureCategory.None;

            if (this.CancelTryCopyBlobContentStream)
            {
                throw new OperationCanceledException();
            }

            if (this.ThrowOnTryCopyBlobContentStream)
            {
                // A non-cancellation, non-GetFileStreamException exception exercises the generic
                // catch in GetFileStreamHandlerAsyncHandler (BlobHydrationFailureCategory.Unexpected).
                throw new InvalidOperationException("Simulated unexpected hydration failure");
            }

            if (this.ThrowIOExceptionDuringCopy)
            {
                // The served length matches the requested length (so no size mismatch), but reading
                // the blob content throws IOException, exercising the LocalIO copy-failure path.
                writeAction(new ThrowOnReadStream(this.FileLength), this.FileLength);
                return true;
            }

            writeAction(
                new MemoryStream(new byte[this.FileLength]),
                this.FileLength);

            return true;
        }

        private sealed class ThrowOnReadStream : Stream
        {
            public ThrowOnReadStream(long length)
            {
                this.Length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length { get; }
            public override long Position { get; set; }

            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated IO failure while reading blob content");
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public override string[] ReadPackFileNames(string packFolderPath, string prefixFilter = "")
        {
            return Array.Empty<string>();
        }

        public override GitProcess.Result IndexPackFile(string packfilePath, GitProcess process)
        {
            return new GitProcess.Result("mocked", null, 0);
        }

        public override void DeleteStaleTempPrefetchPackAndIdxs()
        {
        }

        public override bool TryDownloadPrefetchPacks(GitProcess gitProcess, long latestTimestamp, bool trustPackIndexes, out List<string> packIndexes)
        {
            packIndexes = new List<string>();
            return true;
        }
    }
}
