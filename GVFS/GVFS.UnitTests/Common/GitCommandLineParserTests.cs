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
            new GitCommandLineParser("git status --no-idea").IsVerb("status").ShouldEqual(true);
            new GitCommandLineParser("git status").IsVerb("status").ShouldEqual(true);
            new GitCommandLineParser("git statuses --no-idea").IsVerb("status").ShouldEqual(false);
            new GitCommandLineParser("git statuses").IsVerb("status").ShouldEqual(false);

            new GitCommandLineParser("git add some/file/to/add").IsVerb("add", "status", "reset").ShouldEqual(true);
            new GitCommandLineParser("git adding add").IsVerb("add", "status", "reset").ShouldEqual(false);
            new GitCommandLineParser("git add some/file/to/add").IsVerb("adding", "status", "reset").ShouldEqual(false);
        }
        
        [TestCase]
        public void IsResetSoftOrMixedTests()
        {
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
        public void IsResetHardTests()
        {
            new GitCommandLineParser("git reset --hard").IsResetHard().ShouldEqual(true);

            new GitCommandLineParser("git reset --soft").IsResetHard().ShouldEqual(false);
            new GitCommandLineParser("git reset --mixed").IsResetHard().ShouldEqual(false);

            new GitCommandLineParser("git checkout").IsResetHard().ShouldEqual(false);
            new GitCommandLineParser("git status").IsResetHard().ShouldEqual(false);
        }

        [TestCase]
        public void IsCheckoutWithFilePathsTests()
        {
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
    }
}
