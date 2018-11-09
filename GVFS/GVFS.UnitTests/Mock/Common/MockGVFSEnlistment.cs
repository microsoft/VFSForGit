using GVFS.Common;
using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Git;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockGVFSEnlistment : GVFSEnlistment
    {
        private MockGitProcess gitProcess;

        public MockGVFSEnlistment()
            : base("mock:\\path", "mock://repoUrl", "mock:\\git", gvfsHooksRoot: null, authentication: null)
        {
            this.GitObjectsRoot = "mock:\\path\\.git\\objects";
            this.LocalObjectsRoot = this.GitObjectsRoot;
            this.GitPackRoot = "mock:\\path\\.git\\objects\\pack";
        }

        public MockGVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, string gvfsHooksRoot, MockGitProcess gitProcess)
            : base(enlistmentRoot, repoUrl, gitBinPath, gvfsHooksRoot, authentication: null)
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
