using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class RebaseTests : TestsWithEnlistmentPerTestCase
    {
        public override void CreateEnlistment()
        {
            base.CreateEnlistment();
            GitProcess.Invoke(this.Enlistment.RepoRoot, "config advice.statusUoption false");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "config core.abbrev 12");
        }

        public override void DeleteEnlistment()
        {
            base.DeleteEnlistment();
        }

        [TestCase]
        public void RebaseSmallNoConflicts()
        {
            string sourceBranch = "FunctionalTests/RebaseTestsSource_20170126";

            // Target commit 4600e14c2ca7d6ded8c0d3c670875a8e44955a61 is part of the history of
            // FuncationalTests/20170125
            string targetCommit = "4600e14c2ca7d6ded8c0d3c670875a8e44955a61";

            ControlGitRepo controlGitRepo = ControlGitRepo.Create();
            controlGitRepo.Initialize();
            controlGitRepo.Fetch(sourceBranch);
            controlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand(controlGitRepo, "checkout {0}", sourceBranch);
            this.ValidateGitCommand(controlGitRepo, "rebase {0}", targetCommit);
        }

        [TestCase]
        public void RebaseSmallOneFileConflict()
        {
            string sourceBranch = "FunctionalTests/RebaseTestsSource_20170126";

            // Target commit ab438d5782f6ef9584769362a9877c23eb2d970e is part of the history of
            // FuncationalTests/20170125
            string targetCommit = "ab438d5782f6ef9584769362a9877c23eb2d970e";

            ControlGitRepo controlGitRepo = ControlGitRepo.Create();
            controlGitRepo.Initialize();
            controlGitRepo.Fetch(sourceBranch);
            controlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand(controlGitRepo, "checkout {0}", sourceBranch);
            this.ValidateGitCommand(controlGitRepo, "rebase {0}", targetCommit);
        }

        private void ValidateGitCommand(ControlGitRepo controlGitRepo, string command, params object[] args)
        {
            GitHelpers.ValidateGitCommand(
                this.Enlistment,
                controlGitRepo,
                command,
                args);
        }
    }
}
