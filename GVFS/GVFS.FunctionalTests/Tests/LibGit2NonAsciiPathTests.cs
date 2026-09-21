using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GitProcess = GVFS.FunctionalTests.Tools.GitProcess;

namespace GVFS.FunctionalTests.Tests
{
    /// <summary>
    /// Exercises the real libgit2 (git2.dll) open/read path in <see cref="LibGit2Repo"/> against a
    /// repository whose path and config value contain non-ASCII characters. This is a regression
    /// guard for the string-marshalling bug where the libgit2 P/Invoke declarations used the
    /// implicit <see cref="System.Runtime.InteropServices.CharSet.Ansi"/> default instead of UTF-8:
    /// on a non-UTF-8 Windows code page the ANSI marshaller drops an unmappable character to '?',
    /// so <c>git_repository_open</c> resolves the wrong path and the constructor throws. No
    /// mock-based unit test can catch this because it only manifests through the native P/Invoke.
    /// </summary>
    [TestFixture]
    public class LibGit2NonAsciiPathTests
    {
        // "café_日本語_☃" — Latin-1 accent, CJK, and a symbol outside every common Windows ANSI
        // code page. Built from \u escapes so the reproduction does not depend on this source
        // file's on-disk encoding.
        private const string NonAsciiToken = "caf\u00e9_\u65e5\u672c\u8a9e_\u2603";

        private const string StringConfigKey = "gvfs.functionaltests-nonascii";
        private const string NonAsciiConfigValue = "caf\u00e9-\u65e5\u672c\u8a9e-\u2603";

        private string repoRoot;

        [OneTimeSetUp]
        public void CreateRepo()
        {
            // The ANSI default marshalling only corrupts non-ASCII characters when the active
            // Windows ANSI code page cannot represent them. If the process ANSI code page is
            // UTF-8 (CP65001, the "Beta: Use Unicode UTF-8" setting), ANSI marshalling already
            // produces correct UTF-8, so the bug cannot reproduce and this guard is inconclusive.
            // Log the active code page so a reader of the test results can tell whether this guard
            // actually ran (e.g. CP1252 on the standard CI runners) or was skipped as inconclusive.
            const uint CP_UTF8 = 65001;
            uint activeCodePage = GetACP();
            TestContext.Progress.WriteLine($"{nameof(LibGit2NonAsciiPathTests)}: active ANSI code page = {activeCodePage}");
            if (activeCodePage == CP_UTF8)
            {
                Assert.Ignore(
                    $"Active ANSI code page is UTF-8 (CP{CP_UTF8}); ANSI marshalling already produces UTF-8, so the " +
                    "ANSI-vs-UTF-8 marshalling bug cannot reproduce and this regression guard is inconclusive here. " +
                    "Run on a non-UTF-8 code page (e.g. CP1252, the standard CI runner default) to exercise it.");
            }

            this.repoRoot = Path.Combine(
                Path.GetTempPath(),
                "GVFS.LibGit2NonAsciiPathTests_" + NonAsciiToken + "_" + Path.GetRandomFileName());
            Directory.CreateDirectory(this.repoRoot);

            GitProcess.Invoke(this.repoRoot, "init");
            GitProcess.Invoke(this.repoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.repoRoot, "config user.email \"functional@test.com\"");

            // Write the non-ASCII config value straight to .git/config as UTF-8 (no BOM) so the
            // value round-tripped through libgit2 is not confounded by how git.exe would encode a
            // command-line argument.
            string configPath = Path.Combine(this.repoRoot, ".git", "config");
            File.AppendAllText(
                configPath,
                $"[gvfs]\n\tfunctionaltests-nonascii = {NonAsciiConfigValue}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
        public void OpensRepositoryAtNonAsciiPath()
        {
            // Before the LPUTF8Str marshalling fix, git_repository_open received an ANSI-mangled
            // path, failed, and the constructor threw InvalidDataException.
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                // Reading a known config value proves the RepoHandle is real, not just non-null.
                repo.GetConfigString("user.email").ShouldEqual("functional@test.com");
            }
        }

        [TestCase]
        public void GetConfigStringReturnsNonAsciiValue()
        {
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, this.repoRoot))
            {
                repo.GetConfigString(StringConfigKey).ShouldEqual(NonAsciiConfigValue);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetACP();
    }
}
