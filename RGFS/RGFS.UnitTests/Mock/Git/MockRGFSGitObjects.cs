using RGFS.Common;
using RGFS.Common.Git;
using RGFS.Common.Http;
using System;
using System.IO;
using System.Threading;

namespace RGFS.UnitTests.Mock.Git
{
    public class MockRGFSGitObjects : RGFSGitObjects
    {
        private RGFSContext context;

        public MockRGFSGitObjects(RGFSContext context, GitObjectsHttpRequestor httpGitObjects)
            : base(context, httpGitObjects)
        {
            this.context = context;
        }

        public bool CancelTryCopyBlobContentStream { get; set; }
        public uint FileLength { get; set; }

        public override bool TryDownloadCommit(string objectSha)
        {
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TryDownloadObjects(
                new[] { objectSha },
                onSuccess: (tryCount, response) =>
                {
                    // Add the contents to the mock repo
                    using (StreamReader reader = new StreamReader(response.Stream))
                    {
                        ((MockGitRepo)this.Context.Repository).AddBlob(objectSha, "DownloadedFile", reader.ReadToEnd());
                    }

                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                },
                onFailure: null,
                preferBatchedLooseObjects: false);

            return result.Succeeded && result.Result.Success;
        }

        public override bool TryCopyBlobContentStream(
            string sha,
            CancellationToken cancellationToken,
            Action<Stream, long> writeAction)
        {
            if (this.CancelTryCopyBlobContentStream)
            {
                throw new OperationCanceledException();
            }

            writeAction(
                new MemoryStream(new byte[this.FileLength]), 
                this.FileLength);

            return true;
        }
    }
}
