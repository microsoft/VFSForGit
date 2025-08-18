using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockLibGit2Repo : LibGit2Repo
    {
        public MockLibGit2Repo(ITracer tracer)
            : base()
        {
        }

        public override bool CommitAndRootTreeExists(string commitish, out string treeSha)
        {
            treeSha = string.Empty;
            return false;
        }

        public override bool ObjectExists(string sha)
        {
            return false;
        }

        public override bool TryCopyBlob(string sha, Action<Stream, long> writeAction)
        {
            throw new NotSupportedException();
        }
    }
}
