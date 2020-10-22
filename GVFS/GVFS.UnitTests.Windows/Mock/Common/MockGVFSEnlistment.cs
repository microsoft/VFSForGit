using GVFS.Common;
using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Git;
using System.IO;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockGVFSEnlistment : GVFSEnlistment
    {
        private MockGitProcess gitProcess;

        public MockGVFSEnlistment()
            : base(Path.Combine("mock:", "path"), "mock://repoUrl", Path.Combine("mock:", "git"), authentication: null)
        {
            this.GitObjectsRoot = Path.Combine("mock:", "path", ".git", "objects");
            this.LocalObjectsRoot = this.GitObjectsRoot;
            this.GitPackRoot = Path.Combine("mock:", "path", ".git", "objects", "pack");
        }

        public MockGVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, MockGitProcess gitProcess)
            : base(enlistmentRoot, repoUrl, gitBinPath, authentication: null)
        {
            this.gitProcess = gitProcess;
        }

        public MockGVFSEnlistment(MockGitProcess gitProcess)
            : this()
        {
            this.gitProcess = gitProcess;
        }

        public override string GitObjectsRoot { get; protected set; }

        public override string LocalObjectsRoot { get; protected set; }

        public override string GitPackRoot { get; protected set; }

        public override GitProcess CreateGitProcess()
        {
            return this.gitProcess ?? new MockGitProcess();
        }
    }
}
