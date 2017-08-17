using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using System.IO;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGVFSGitObjects : GVFSGitObjects
    {
        private GVFSContext context;

        public MockGVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor httpGitObjects)
            : base(context, httpGitObjects)
        {
            this.context = context;
        }

        public override bool TryDownloadAndSaveCommit(string objectSha, int commitDepth)
        {
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TryDownloadObjects(
                new[] { objectSha },
                commitDepth,
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
    }
}
