using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests
{
    /// <summary>
    /// Tests for the authentication step of 'gvfs clone', which runs before the
    /// enlistment's src folder exists.
    /// </summary>
    [TestFixture]
    public class CloneAuthTests
    {
        private const int GVFSGenericError = 3;

        /// <summary>
        /// The message GVFS reports when it cannot reach the server at all. It must not
        /// appear in these tests: it means the clone failed before it asked for
        /// credentials, so the test proved nothing.
        /// </summary>
        private const string ConfigQueryFailed = "Unable to query /gvfs/config";

        private string testRoot;

        /// <summary>
        /// A URL that always requires authentication, so that 'gvfs clone' runs
        /// 'git credential fill' instead of succeeding anonymously. The project does
        /// not exist, but Azure DevOps answers with 401 before it checks existence,
        /// which is what this test needs. The host comes from the configured test
        /// repo so that no new network dependency is introduced.
        /// </summary>
        private static string AuthRequiredRepoUrl
        {
            get
            {
                Uri repoToClone = new Uri(Properties.Settings.Default.RepoToClone);
                return repoToClone.GetLeftPart(UriPartial.Authority) + "/NoSuchProject/_git/NoSuchRepo";
            }
        }

        [SetUp]
        public void CreateTestRoot()
        {
            this.testRoot = Path.Combine(
                Properties.Settings.Default.EnlistmentRoot,
                "CloneAuthTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void DeleteTestRoot()
        {
            RepositoryHelpers.DeleteTestDirectory(this.testRoot);
        }

        /// <summary>
        /// 'gvfs clone' must reach the credential helper when the user's global config
        /// contains an 'includeIf "gitdir:..."' section. Git resolves the --git-dir path
        /// before it evaluates the condition, so passing the not-yet-created src folder
        /// makes git fail with "Invalid path" instead of asking for credentials.
        /// </summary>
        [TestCase]
        public void CloneAuthenticatesWhenGitConfigContainsIncludeIfGitDir()
        {
            string globalConfig = this.WriteGlobalConfig(includeIfGitDir: true);
            string enlistmentRoot = Path.Combine(this.testRoot, "enlistment");

            ProcessResult result = this.RunClone(enlistmentRoot, globalConfig);

            ShouldFailInTheCredentialHelper(result);
        }

        /// <summary>
        /// Control for <see cref="CloneAuthenticatesWhenGitConfigContainsIncludeIfGitDir"/>.
        /// Without the includeIf section the same clone must reach the credential helper.
        /// </summary>
        [TestCase]
        public void CloneAuthenticatesWhenGitConfigHasNoIncludeIf()
        {
            string globalConfig = this.WriteGlobalConfig(includeIfGitDir: false);
            string enlistmentRoot = Path.Combine(this.testRoot, "enlistment");

            ProcessResult result = this.RunClone(enlistmentRoot, globalConfig);

            ShouldFailInTheCredentialHelper(result);
        }

        /// <summary>
        /// Asserts that the clone reached the credential helper and failed there.
        /// </summary>
        /// <remarks>
        /// The positive assertions matter as much as the negative ones. A clone that
        /// cannot reach the server also exits with <see cref="GVFSGenericError"/> and
        /// prints neither "Invalid path" nor "No such file or directory", so a test that
        /// only checks for the absence of those strings passes when the network is down
        /// and proves nothing.
        /// </remarks>
        private static void ShouldFailInTheCredentialHelper(ProcessResult result)
        {
            string output = result.Output + result.Errors;

            // No credentials are available, so the clone must fail.
            result.ExitCode.ShouldEqual(GVFSGenericError, output);

            // It must have got as far as authentication, and the server must have
            // answered, otherwise the rest of this test is meaningless.
            output.ShouldContain("Authenticating...Failed");
            output.ShouldNotContain(false, ConfigQueryFailed);

            // It must fail because the credential helper produced no password, not
            // because git could not resolve the src folder that clone has not created
            // yet.
            output.ShouldNotContain(true, "Invalid path", "No such file or directory");
        }

        private string WriteGlobalConfig(bool includeIfGitDir)
        {
            // An empty helper value clears the credential helper list, so git fails
            // immediately instead of showing an interactive credential prompt.
            string contents = "[credential]\n\thelper =\n";

            if (includeIfGitDir)
            {
                string includedConfig = Path.Combine(this.testRoot, "included.gitconfig");
                File.WriteAllText(includedConfig, string.Empty);

                // The condition never matches. Git still resolves the repository's
                // gitdir before it compares the pattern, which is what triggers the
                // failure this test guards against.
                contents +=
                    "[includeIf \"gitdir:NoSuchDirectory/\"]\n" +
                    "\tpath = " + includedConfig.Replace('\\', '/') + "\n";
            }

            string globalConfig = Path.Combine(this.testRoot, "global.gitconfig");
            File.WriteAllText(globalConfig, contents);
            return globalConfig;
        }

        private ProcessResult RunClone(string enlistmentRoot, string globalConfig)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = $"clone {AuthRequiredRepoUrl} \"{enlistmentRoot}\" --no-mount --no-prefetch";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = this.testRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;

            // Point git at the test's config instead of the config of the user running
            // the tests, so the test controls whether includeIf is present.
            processInfo.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = globalConfig;
            processInfo.EnvironmentVariables["GIT_CONFIG_SYSTEM"] = Path.Combine(this.testRoot, "system.gitconfig");
            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            return ProcessHelper.Run(processInfo);
        }
    }
}
