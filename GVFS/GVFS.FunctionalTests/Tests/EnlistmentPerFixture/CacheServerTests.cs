using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class CacheServerTests : TestsWithEnlistmentPerFixture
    {
        [TestCase]
        public void SettingGitConfigChangesCacheServer()
        {
            const string ExpectedUrl = "https://myCache";

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config gvfs.cache-server " + ExpectedUrl);
            result.ExitCode.ShouldEqual(0, result.Errors);

            string getoutput = this.Enlistment.CacheServer("--get");
            getoutput.ShouldNotBeNull();
            string currentCache = getoutput.Trim().Substring(getoutput.LastIndexOf('\t') + 1);
            currentCache.ShouldContain(ExpectedUrl);
        }

        [TestCase]
        public void SettingACacheReflectsChangesInCacheServerGet()
        {
            string getOutput = this.Enlistment.CacheServer("--get");
            getOutput.ShouldNotBeNull();

            this.Enlistment.CacheServer("--set https://fake");

            this.Enlistment.CacheServer("--get").ShouldNotEqual(getOutput);
        }
    }
}
