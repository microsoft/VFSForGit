using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Common.Git
{
    /// <summary>
    /// Verifies which repository the credential verbs run against.
    /// </summary>
    /// <remarks>
    /// Credential helpers must see repo-local configuration, so the credential verbs
    /// normally run against the enlistment's .git folder. During 'gvfs clone' that
    /// folder does not exist yet, and git refuses to resolve a --git-dir that is not
    /// there when the user's config contains an 'includeIf "gitdir:..."' section.
    /// </remarks>
    [TestFixture]
    public class GitProcessCredentialTests
    {
        private const string CredentialFillCommandPrefix = "-c " + GitConfigSetting.CredentialUseHttpPath + "=true credential fill";
        private const string CredentialApproveCommandPrefix = "-c " + GitConfigSetting.CredentialUseHttpPath + "=true credential approve";
        private const string CredentialRejectCommandPrefix = "-c " + GitConfigSetting.CredentialUseHttpPath + "=true credential reject";
        private const string RepoUrl = "mock://repoUrl";

        private string testRoot;

        [SetUp]
        public void CreateTestRoot()
        {
            this.testRoot = Path.Combine(Path.GetTempPath(), "GitProcessCredentialTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void DeleteTestRoot()
        {
            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        [TestCase]
        public void CredentialVerbDoesNotUseGitDirWhenDotGitIsMissing()
        {
            // 'gvfs clone' authenticates before it creates the enlistment, so the
            // credential verb must not name a .git folder that does not exist yet.
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");

            string dotGitDirectoryUsed = this.RunCredentialFill(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldBeNull();
        }

        [TestCase]
        public void CredentialVerbUsesGitDirWhenDotGitIsAFolder()
        {
            // In an established enlistment the credential helper must still see the
            // repository's local configuration.
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");
            string dotGitRoot = Path.Combine(workingDirectoryRoot, ".git");
            Directory.CreateDirectory(dotGitRoot);

            string dotGitDirectoryUsed = this.RunCredentialFill(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldEqual(dotGitRoot);
        }

        [TestCase]
        public void CredentialVerbUsesGitDirWhenDotGitIsAWorktreeFile()
        {
            // A linked worktree has a .git file that points at the real git directory
            // instead of a .git folder. That is still a valid repository.
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");
            Directory.CreateDirectory(workingDirectoryRoot);
            string dotGitRoot = Path.Combine(workingDirectoryRoot, ".git");
            File.WriteAllText(dotGitRoot, "gitdir: " + Path.Combine(this.testRoot, "worktrees", "src"));

            string dotGitDirectoryUsed = this.RunCredentialFill(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldEqual(dotGitRoot);
        }

        [TestCase]
        public void StoreCredentialDoesNotUseGitDirWhenDotGitIsMissing()
        {
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");

            string dotGitDirectoryUsed = this.RunStoreCredential(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldBeNull();
        }

        [TestCase]
        public void StoreCredentialUsesGitDirWhenDotGitIsAFolder()
        {
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");
            string dotGitRoot = Path.Combine(workingDirectoryRoot, ".git");
            Directory.CreateDirectory(dotGitRoot);

            string dotGitDirectoryUsed = this.RunStoreCredential(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldEqual(dotGitRoot);
        }

        [TestCase]
        public void DeleteCredentialDoesNotUseGitDirWhenDotGitIsMissing()
        {
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");

            string dotGitDirectoryUsed = this.RunDeleteCredential(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldBeNull();
        }

        [TestCase]
        public void DeleteCredentialUsesGitDirWhenDotGitIsAFolder()
        {
            string workingDirectoryRoot = Path.Combine(this.testRoot, "src");
            string dotGitRoot = Path.Combine(workingDirectoryRoot, ".git");
            Directory.CreateDirectory(dotGitRoot);

            string dotGitDirectoryUsed = this.RunDeleteCredential(workingDirectoryRoot);

            dotGitDirectoryUsed.ShouldEqual(dotGitRoot);
        }

        private static MockGitProcess CreateGitProcess(string workingDirectoryRoot, string verbCommandPrefix)
        {
            MockGitProcess gitProcess = new MockGitProcess(Path.Combine("mock:", "git"), workingDirectoryRoot);
            gitProcess.SetExpectedCommandResult(
                verbCommandPrefix,
                () => new GitProcess.Result("username=mockUser\npassword=mockPassword\n", string.Empty, GitProcess.Result.SuccessCode),
                matchPrefix: true);

            return gitProcess;
        }

        /// <summary>
        /// Returns the --git-dir used by the single git invocation the caller triggered.
        /// </summary>
        /// <remarks>
        /// Takes the count from before the invocation so that the assertion stays valid
        /// if a credential verb ever makes more than one git call.
        /// </remarks>
        private static string SingleDotGitDirectoryUsed(MockGitProcess gitProcess, int countBeforeInvocation)
        {
            gitProcess.DotGitDirectoriesUsed.Count.ShouldEqual(
                countBeforeInvocation + 1,
                "Expected exactly one git invocation for the credential verb");

            return gitProcess.DotGitDirectoriesUsed[countBeforeInvocation];
        }

        private string RunCredentialFill(string workingDirectoryRoot)
        {
            MockGitProcess gitProcess = CreateGitProcess(workingDirectoryRoot, CredentialFillCommandPrefix);
            int countBeforeInvocation = gitProcess.DotGitDirectoriesUsed.Count;

            gitProcess.TryGetCredential(
                new MockTracer(),
                RepoUrl,
                out string username,
                out string password,
                out string error)
                .ShouldBeTrue(error);

            gitProcess.CommandsRun.ShouldContain(x => x.StartsWith(CredentialFillCommandPrefix, StringComparison.Ordinal));
            return SingleDotGitDirectoryUsed(gitProcess, countBeforeInvocation);
        }

        private string RunStoreCredential(string workingDirectoryRoot)
        {
            MockGitProcess gitProcess = CreateGitProcess(workingDirectoryRoot, CredentialApproveCommandPrefix);
            int countBeforeInvocation = gitProcess.DotGitDirectoriesUsed.Count;

            gitProcess.TryStoreCredential(
                new MockTracer(),
                RepoUrl,
                "mockUser",
                "mockPassword",
                out string error)
                .ShouldBeTrue(error);

            gitProcess.CommandsRun.ShouldContain(x => x.StartsWith(CredentialApproveCommandPrefix, StringComparison.Ordinal));
            return SingleDotGitDirectoryUsed(gitProcess, countBeforeInvocation);
        }

        private string RunDeleteCredential(string workingDirectoryRoot)
        {
            MockGitProcess gitProcess = CreateGitProcess(workingDirectoryRoot, CredentialRejectCommandPrefix);
            int countBeforeInvocation = gitProcess.DotGitDirectoriesUsed.Count;

            gitProcess.TryDeleteCredential(
                new MockTracer(),
                RepoUrl,
                "mockUser",
                "mockPassword",
                out string error)
                .ShouldBeTrue(error);

            gitProcess.CommandsRun.ShouldContain(x => x.StartsWith(CredentialRejectCommandPrefix, StringComparison.Ordinal));
            return SingleDotGitDirectoryUsed(gitProcess, countBeforeInvocation);
        }
    }
}
