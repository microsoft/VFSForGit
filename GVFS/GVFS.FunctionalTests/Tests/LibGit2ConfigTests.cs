using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using GitProcess = GVFS.FunctionalTests.Tools.GitProcess;

namespace GVFS.FunctionalTests.Tests
{
    /// <summary>
    /// Exercises the real libgit2 (git2.dll) config-read path in <see cref="LibGit2Repo"/>
    /// against a plain on-disk git repository. This is a regression guard for the
    /// "get_string called on a live config object" failure, which no mock-based unit
    /// test can catch because it only manifests through the native P/Invoke.
    /// </summary>
    [TestFixture]
    public class LibGit2ConfigTests
    {
        private const string StringConfigKey = "gvfs.functionaltests-teststring";
        private const string StringConfigValue = "libgit2-value-42";
        private const string BoolConfigKey = "gvfs.functionaltests-testbool";
        private const string MissingConfigKey = "gvfs.functionaltests-missing";

        private string repoRoot;

        [OneTimeSetUp]
        public void CreateRepo()
        {
            this.repoRoot = Path.Combine(Path.GetTempPath(), "GVFS.LibGit2ConfigTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(this.repoRoot);

            GitProcess.Invoke(this.repoRoot, "init");
            GitProcess.Invoke(this.repoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.repoRoot, "config user.email \"functional@test.com\"");
            GitProcess.Invoke(this.repoRoot, $"config {StringConfigKey} {StringConfigValue}");
            GitProcess.Invoke(this.repoRoot, $"config {BoolConfigKey} true");
        }

        [OneTimeTearDown]
        public void DeleteRepo()
        {
            if (this.repoRoot != null)
            {
                RepositoryHelpers.DeleteTestDirectory(this.repoRoot);
            }
        }

        [TestCase]
        public void GetConfigStringReturnsValueFromLiveConfig()
        {
            // Before the snapshot fix this threw LibGit2Exception
            // ("get_string called on a live config object") and callers silently
            // fell back to their default value.
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                repo.GetConfigString(StringConfigKey).ShouldEqual(StringConfigValue);
            }
        }

        [TestCase]
        public void GetConfigStringReturnsNullWhenKeyMissing()
        {
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                repo.GetConfigString(MissingConfigKey).ShouldBeNull();
            }
        }

        [TestCase]
        public void GetConfigBoolReturnsValueFromLiveConfig()
        {
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                repo.GetConfigBool(BoolConfigKey).ShouldEqual(true);
            }
        }
    }
}
