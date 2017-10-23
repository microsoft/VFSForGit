using GVFS.Common;
using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Git;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockEnlistment : Enlistment
    {
        private MockGitProcess gitProcess;

        public MockEnlistment()
            : base("mock:\\path", "mock:\\path", "mock:\\repoUrl", "mock:\\git", null)
        {
            this.GitObjectsRoot = "mock:\\path\\.git\\objects";
            this.GitPackRoot = "mock:\\path\\.git\\objects\\pack";
        }

        public MockEnlistment(MockGitProcess gitProcess)
            : this()
        {
            this.gitProcess = gitProcess;
        }
        
        public override string GitObjectsRoot { get; }

        public override string GitPackRoot { get; }

        public override GitProcess CreateGitProcess()
        {
            return this.gitProcess ?? new MockGitProcess();
        }
    }
}
