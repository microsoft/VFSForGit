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
            // 5d299512450f4029d7a1fe8d67e833b84247d393 is the tip of FunctionalTests/RebaseTestsSource_20170130
            string sourceCommit = "5d299512450f4029d7a1fe8d67e833b84247d393";

            // Target commit 47fabb534c35af40156db6e8365165cb04f9dd75 is part of the history of
            // FunctionalTests/20170130
            string targetCommit = "47fabb534c35af40156db6e8365165cb04f9dd75";

            ControlGitRepo controlGitRepo = ControlGitRepo.Create();
            controlGitRepo.Initialize();
            controlGitRepo.Fetch(sourceCommit);
            controlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand(controlGitRepo, "checkout {0}", sourceCommit);
            this.ValidateGitCommand(controlGitRepo, "rebase {0}", targetCommit);
        }

        [TestCase]
        public void RebaseSmallOneFileConflict()
        {
            // 5d299512450f4029d7a1fe8d67e833b84247d393 is the tip of FunctionalTests/RebaseTestsSource_20170130
            string sourceCommit = "5d299512450f4029d7a1fe8d67e833b84247d393";

            // Target commit 99fc72275f950b0052c8548bbcf83a851f2b4467 is part of the history of
            // FunctionalTests/20170130
            string targetCommit = "99fc72275f950b0052c8548bbcf83a851f2b4467";

            ControlGitRepo controlGitRepo = ControlGitRepo.Create();
            controlGitRepo.Initialize();
            controlGitRepo.Fetch(sourceCommit);
            controlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand(controlGitRepo, "checkout {0}", sourceCommit);
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
