using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Physical.Git;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Mock.Physical.Git
{
    public class MockGVFSGitObjects : GVFSGitObjects
    {
        private GVFSContext context;

        public MockGVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor httpGitObjects)
            : base(context, httpGitObjects)
        {
            this.context = context;
        }

        public override bool TryDownloadAndSaveCommits(IEnumerable<string> objectShas, int commitDepth)
        {
            bool output = true;
            foreach (string sha in objectShas)
            {
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TryDownloadObjects(
                    new[] { sha },
                    commitDepth,
                    onSuccess: (tryCount, response) =>
                    {
                        // Add the contents to the mock repo
                        using (StreamReader reader = new StreamReader(response.Stream))
                        {
                            ((MockGitRepo)this.Context.Repository).AddBlob(sha, "DownloadedFile", reader.ReadToEnd());
                        }

                        return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                    },
                    onFailure: null,
                    preferBatchedLooseObjects: false);

                return result.Succeeded && result.Result.Success;
            }

            return output;
        }
    }
}
