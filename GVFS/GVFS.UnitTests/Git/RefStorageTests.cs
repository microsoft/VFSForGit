using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class RefStorageTests
    {
        private const string ConfigCommand = "config --local " + RefStorage.RefStorageConfigName;

        [TestCase]
        public void IsReftableIsTrueForReftableValue()
        {
            RefStorage.IsReftable("reftable").ShouldEqual(true);
        }

        [TestCase]
        public void IsReftableIsTrueForMixedCaseReftableValue()
        {
            RefStorage.IsReftable("Reftable").ShouldEqual(true);
        }

        [TestCase]
        public void IsReftableIsTrueForReftableValueWithSurroundingWhitespace()
        {
            RefStorage.IsReftable(" reftable \n").ShouldEqual(true);
        }

        [TestCase]
        public void IsReftableIsFalseForFilesValue()
        {
            RefStorage.IsReftable("files").ShouldEqual(false);
        }

        [TestCase]
        public void IsReftableIsFalseForNullValue()
        {
            RefStorage.IsReftable(null).ShouldEqual(false);
        }

        [TestCase]
        public void IsReftableIsFalseForEmptyValue()
        {
            RefStorage.IsReftable(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsReftableRepoIsTrueWhenConfigIsReftable()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result("reftable\n", string.Empty, GitProcess.Result.SuccessCode));

            RefStorage.IsReftableRepo(git).ShouldEqual(true);
        }

        [TestCase]
        public void IsReftableRepoIsFalseWhenConfigIsFiles()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result("files\n", string.Empty, GitProcess.Result.SuccessCode));

            RefStorage.IsReftableRepo(git).ShouldEqual(false);
        }

        [TestCase]
        public void IsReftableRepoIsFalseWhenConfigIsMissing()
        {
            MockGitProcess git = new MockGitProcess();

            // A missing config key causes 'git config' to exit non-zero with no stderr output,
            // which is the observed behavior for a files-format repo that has no
            // extensions.refstorage key.
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            RefStorage.IsReftableRepo(git).ShouldEqual(false);
        }

        [TestCase]
        public void IsReftableRepoSwallowsGenuineReadFailureAndReturnsFalse()
        {
            MockGitProcess git = new MockGitProcess();

            // The non-Try overload deliberately swallows a genuine config-read failure
            // (non-zero exit with real stderr content) and reports "not reftable", matching
            // GitProcess.TryGetFromConfig's "failure == not set" convention. Callers that
            // need to distinguish a read failure use TryIsReftableRepo instead.
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result(string.Empty, "fatal: not a git repository", GitProcess.Result.GenericFailureCode));

            RefStorage.IsReftableRepo(git).ShouldEqual(false);
        }

        [TestCase]
        public void TryIsReftableRepoSucceedsWithNoErrorWhenConfigIsReftable()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result("reftable\n", string.Empty, GitProcess.Result.SuccessCode));

            RefStorage.TryIsReftableRepo(git, out bool isReftable, out string error).ShouldEqual(true);
            isReftable.ShouldEqual(true);
            error.ShouldEqual(string.Empty);
        }

        [TestCase]
        public void TryIsReftableRepoSucceedsWithNoErrorWhenConfigIsMissing()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            RefStorage.TryIsReftableRepo(git, out bool isReftable, out string error).ShouldEqual(true);
            isReftable.ShouldEqual(false);
            error.ShouldEqual(string.Empty);
        }

        [TestCase]
        public void TryIsReftableRepoFailsAndReportsErrorWhenConfigReadFails()
        {
            MockGitProcess git = new MockGitProcess();

            // A genuine 'git config' failure (non-zero exit with real stderr content, as
            // opposed to a missing key) should be surfaced distinctly rather than silently
            // treated the same as "not reftable".
            git.SetExpectedCommandResult(
                ConfigCommand,
                () => new GitProcess.Result(string.Empty, "fatal: not a git repository", GitProcess.Result.GenericFailureCode));

            RefStorage.TryIsReftableRepo(git, out bool isReftable, out string error).ShouldEqual(false);
            isReftable.ShouldEqual(false);
            error.ShouldNotBeNull();
        }
    }
}
