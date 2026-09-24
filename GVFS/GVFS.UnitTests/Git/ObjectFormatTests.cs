using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class ObjectFormatTests
    {
        private const string ConfigCommand = "config --local " + ObjectFormat.ObjectFormatConfigName;

        [TestCase]
        public void IsSha256IsTrueForSha256Value()
        {
            ObjectFormat.IsSha256("sha256").ShouldEqual(true);
        }

        [TestCase]
        public void IsSha256IsTrueForMixedCaseSha256Value()
        {
            ObjectFormat.IsSha256("SHA256").ShouldEqual(true);
        }

        [TestCase]
        public void IsSha256IsTrueForSha256ValueWithSurroundingWhitespace()
        {
            ObjectFormat.IsSha256(" sha256 \n").ShouldEqual(true);
        }

        [TestCase]
        public void IsSha256IsFalseForSha1Value()
        {
            ObjectFormat.IsSha256("sha1").ShouldEqual(false);
        }

        [TestCase]
        public void IsSha256IsFalseForNullValue()
        {
            ObjectFormat.IsSha256(null).ShouldEqual(false);
        }

        [TestCase]
        public void IsSha256IsFalseForEmptyValue()
        {
            ObjectFormat.IsSha256(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsSha256RepoIsTrueWhenConfigIsSha256()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result("sha256\n", string.Empty, GitProcess.Result.SuccessCode));

            ObjectFormat.IsSha256Repo(git).ShouldEqual(true);
        }

        [TestCase]
        public void IsSha256RepoIsFalseWhenConfigIsSha1()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result("sha1\n", string.Empty, GitProcess.Result.SuccessCode));

            ObjectFormat.IsSha256Repo(git).ShouldEqual(false);
        }

        [TestCase]
        public void IsSha256RepoIsFalseWhenConfigIsMissing()
        {
            MockGitProcess git = new MockGitProcess();

            // A missing config key causes 'git config' to exit non-zero with no stderr output,
            // which is the observed behavior for a SHA1 repo that predates extensions.objectformat.
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            ObjectFormat.IsSha256Repo(git).ShouldEqual(false);
        }
    }
}
