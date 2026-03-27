using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitCommandLineParserTests
    {
        [TestCase]
        public void IsVerbTests()
        {
            new GitCommandLineParser("gits status --no-idea").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);

            new GitCommandLineParser("git status --no-idea").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(true);
            new GitCommandLineParser("git status").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(true);
            new GitCommandLineParser("git statuses --no-idea").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git statuses").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);

            new GitCommandLineParser("git add").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git checkout").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git clean").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git commit").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git mv").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git reset").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git stage").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);
            new GitCommandLineParser("git update-index").IsVerb(GitCommandLineParser.Verbs.Status).ShouldEqual(false);

            new GitCommandLineParser("git add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(true);
            new GitCommandLineParser("git checkout").IsVerb(GitCommandLineParser.Verbs.Checkout).ShouldEqual(true);
            new GitCommandLineParser("git commit").IsVerb(GitCommandLineParser.Verbs.Commit).ShouldEqual(true);
            new GitCommandLineParser("git mv").IsVerb(GitCommandLineParser.Verbs.Move).ShouldEqual(true);
            new GitCommandLineParser("git reset").IsVerb(GitCommandLineParser.Verbs.Reset).ShouldEqual(true);
            new GitCommandLineParser("git stage").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(true);
            new GitCommandLineParser("git update-index").IsVerb(GitCommandLineParser.Verbs.UpdateIndex).ShouldEqual(true);
            new GitCommandLineParser("git updateindex").IsVerb(GitCommandLineParser.Verbs.UpdateIndex).ShouldEqual(false);

            new GitCommandLineParser("git add some/file/to/add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(true);
            new GitCommandLineParser("git stage some/file/to/add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(true);
            new GitCommandLineParser("git adds some/file/to/add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(false);
            new GitCommandLineParser("git stages some/file/to/add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(false);
            new GitCommandLineParser("git adding add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(false);
            new GitCommandLineParser("git adding add").IsVerb(GitCommandLineParser.Verbs.AddOrStage).ShouldEqual(false);
            new GitCommandLineParser("git adding add").IsVerb(GitCommandLineParser.Verbs.Other).ShouldEqual(true);
        }

        [TestCase]
        public void IsResetSoftOrMixedTests()
        {
            new GitCommandLineParser("gits reset --soft").IsResetSoftOrMixed().ShouldEqual(false);

            new GitCommandLineParser("git reset --soft").IsResetSoftOrMixed().ShouldEqual(true);
            new GitCommandLineParser("git reset --mixed").IsResetSoftOrMixed().ShouldEqual(true);
            new GitCommandLineParser("git reset").IsResetSoftOrMixed().ShouldEqual(true);

            new GitCommandLineParser("git reset --hard").IsResetSoftOrMixed().ShouldEqual(false);
            new GitCommandLineParser("git reset --keep").IsResetSoftOrMixed().ShouldEqual(false);
            new GitCommandLineParser("git reset --merge").IsResetSoftOrMixed().ShouldEqual(false);

            new GitCommandLineParser("git checkout").IsResetSoftOrMixed().ShouldEqual(false);
            new GitCommandLineParser("git status").IsResetSoftOrMixed().ShouldEqual(false);
        }

        [TestCase]
        public void IsCheckoutWithFilePathsTests()
        {
            new GitCommandLineParser("gits checkout branch -- file").IsCheckoutWithFilePaths().ShouldEqual(false);

            new GitCommandLineParser("git checkout branch -- file").IsCheckoutWithFilePaths().ShouldEqual(true);
            new GitCommandLineParser("git checkout branch -- file1 file2").IsCheckoutWithFilePaths().ShouldEqual(true);
            new GitCommandLineParser("git checkout HEAD -- file").IsCheckoutWithFilePaths().ShouldEqual(true);

            new GitCommandLineParser("git checkout HEAD file").IsCheckoutWithFilePaths().ShouldEqual(true);
            new GitCommandLineParser("git checkout HEAD file1 file2").IsCheckoutWithFilePaths().ShouldEqual(true);

            new GitCommandLineParser("git checkout branch file").IsCheckoutWithFilePaths().ShouldEqual(false);
            new GitCommandLineParser("git checkout branch").IsCheckoutWithFilePaths().ShouldEqual(false);
            new GitCommandLineParser("git checkout HEAD").IsCheckoutWithFilePaths().ShouldEqual(false);

            new GitCommandLineParser("git checkout -b topic").IsCheckoutWithFilePaths().ShouldEqual(false);

            new GitCommandLineParser("git checkout -b topic --").IsCheckoutWithFilePaths().ShouldEqual(false);
            new GitCommandLineParser("git checkout HEAD --").IsCheckoutWithFilePaths().ShouldEqual(false);
            new GitCommandLineParser("git checkout HEAD -- ").IsCheckoutWithFilePaths().ShouldEqual(false);
        }

        [TestCase]
        public void IsSerializedStatusTests()
        {
            new GitCommandLineParser("git status --serialized=some/file").IsSerializedStatus().ShouldEqual(true);
            new GitCommandLineParser("git status --serialized").IsSerializedStatus().ShouldEqual(true);

            new GitCommandLineParser("git checkout branch -- file").IsSerializedStatus().ShouldEqual(false);
            new GitCommandLineParser("git status").IsSerializedStatus().ShouldEqual(false);
            new GitCommandLineParser("git checkout --serialized").IsSerializedStatus().ShouldEqual(false);
            new GitCommandLineParser("git checkout --serialized=some/file").IsSerializedStatus().ShouldEqual(false);
            new GitCommandLineParser("gits status --serialized=some/file").IsSerializedStatus().ShouldEqual(false);
        }

        [TestCase]
        public void TryGetBranchSwitchTargetTests()
        {
            // Invalid/non-checkout commands
            new GitCommandLineParser(null).TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("notgit checkout main").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git status").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git fetch origin").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);

            // Simple branch switches
            new GitCommandLineParser("git checkout main").TryGetBranchSwitchTarget(out string target).ShouldEqual(true);
            target.ShouldEqual("main");
            new GitCommandLineParser("git checkout origin/main").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("origin/main");
            new GitCommandLineParser("git switch feature-branch").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("feature-branch");
            new GitCommandLineParser("git checkout -f main").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("main");
            new GitCommandLineParser("git checkout --track origin/feature").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("origin/feature");

            // File checkouts (not branch switches)
            new GitCommandLineParser("git checkout -- somefile.txt").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout main -- somefile.txt").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout file1.txt file2.txt").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);

            // New branch: no start point = no churn, with start point = churn
            new GitCommandLineParser("git checkout -b new-branch").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout -b topic2 origin/main").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("origin/main");
            new GitCommandLineParser("git checkout -B topic2 origin/main").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("origin/main");
            new GitCommandLineParser("git switch -c topic2 origin/main").TryGetBranchSwitchTarget(out target).ShouldEqual(true);
            target.ShouldEqual("origin/main");

            // Modes that are not branch switches
            new GitCommandLineParser("git checkout --detach abc1234").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout --orphan new-root").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout -p").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
            new GitCommandLineParser("git checkout --patch").TryGetBranchSwitchTarget(out string _).ShouldEqual(false);
        }
    }
}
