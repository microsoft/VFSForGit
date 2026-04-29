using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class PostIndexChangedHookTests
    {
        // Exit code from common.h ReturnCode::NotInGVFSEnlistment.
        // The hook dies with this code when it can't find a .gvfs folder.
        private const int NotInGVFSEnlistment = 3;

        // The hook exe is built to the same output root as the test runner.
        // Walk up from the unit test output dir to find the hook exe under
        // the shared build output tree.
        private static readonly string HookExePath = FindHookExe();

        private static string FindHookExe()
        {
            // Test runner lives at: out\GVFS.UnitTests\bin\Debug\net471\win-x64\
            // Hook exe lives at:    out\GVFS.PostIndexChangedHook\bin\x64\Debug\
            string testDir = Path.GetDirectoryName(Environment.ProcessPath);
            string outDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
            string hookPath = Path.Combine(outDir, "GVFS.PostIndexChangedHook", "bin", "x64", "Debug", "GVFS.PostIndexChangedHook.exe");

            // Also check via VFS_OUTDIR if available
            if (!File.Exists(hookPath))
            {
                string vfsOutDir = Environment.GetEnvironmentVariable("VFS_OUTDIR");
                if (!string.IsNullOrEmpty(vfsOutDir))
                {
                    hookPath = Path.Combine(vfsOutDir, "GVFS.PostIndexChangedHook", "bin", "x64", "Debug", "GVFS.PostIndexChangedHook.exe");
                }
            }

            return hookPath;
        }

        [SetUp]
        public void EnsureHookExists()
        {
            if (!File.Exists(HookExePath))
            {
                Assert.Ignore($"Hook exe not found at {HookExePath} — build the full solution first.");
            }
        }

        /// <summary>
        /// When GIT_INDEX_FILE points to a non-canonical (temp) index,
        /// the hook should exit immediately with code 0 without trying
        /// to connect to the GVFS pipe.
        /// </summary>
        [TestCase("C:\\repo\\.git\\tmp_index_1234", "C:\\repo\\.git")]
        [TestCase("/repo/.git/some_other_index", "/repo/.git")]
        [TestCase("D:\\src\\.git\\index.lock", "D:\\src\\.git")]
        [TestCase("C:\\tmp\\scratch_index", "C:\\repo\\.git")]
        public void SkipsNotification_WhenIndexIsNonCanonical(string indexFile, string gitDir)
        {
            int exitCode = RunHook(indexFile, gitDir);
            Assert.AreEqual(0, exitCode, "Hook should exit 0 (skip) for non-canonical index");
        }

        /// <summary>
        /// When GIT_INDEX_FILE matches the canonical $GIT_DIR/index,
        /// the hook should NOT skip — it should proceed and attempt the
        /// pipe connection. Outside a GVFS mount (WorkingDirectory is
        /// %TEMP%), the hook fails with NotInGVFSEnlistment, proving
        /// the guard did not fire.
        /// </summary>
        [TestCase("C:\\repo\\.git\\index", "C:\\repo\\.git")]
        [TestCase("C:\\repo\\.git/index", "C:\\repo\\.git\\")]
        public void DoesNotSkip_WhenIndexIsCanonical(string indexFile, string gitDir)
        {
            int exitCode = RunHook(indexFile, gitDir);
            Assert.AreEqual(NotInGVFSEnlistment, exitCode,
                "Hook should NOT skip for canonical index (NotInGVFSEnlistment = guard didn't fire)");
        }

        /// <summary>
        /// When GIT_INDEX_FILE is not set at all, the hook should NOT
        /// skip — this is the normal case where git writes the default index.
        /// </summary>
        [Test]
        public void DoesNotSkip_WhenGitIndexFileNotSet()
        {
            int exitCode = RunHook(null, "C:\\repo\\.git");
            Assert.AreEqual(NotInGVFSEnlistment, exitCode,
                "Hook should NOT skip when GIT_INDEX_FILE is unset");
        }

        /// <summary>
        /// When GIT_INDEX_FILE is set but GIT_DIR is empty/missing,
        /// the hook should NOT skip — err on the side of correctness
        /// when the environment is unexpected.
        /// </summary>
        [TestCase("C:\\repo\\.git\\tmp_index", null)]
        [TestCase("C:\\repo\\.git\\tmp_index", "")]
        public void DoesNotSkip_WhenGitDirMissing(string indexFile, string gitDir)
        {
            int exitCode = RunHook(indexFile, gitDir);
            Assert.AreEqual(NotInGVFSEnlistment, exitCode,
                "Hook should NOT skip when GIT_DIR is absent — err on the side of correctness");
        }

        /// <summary>
        /// Case-insensitive matching: mixed-case paths that resolve to
        /// the canonical index should NOT skip.
        /// </summary>
        [Test]
        public void DoesNotSkip_WhenCanonicalPathDiffersOnlyInCase()
        {
            int exitCode = RunHook("C:\\Repo\\.GIT\\INDEX", "C:\\Repo\\.GIT");
            Assert.AreEqual(NotInGVFSEnlistment, exitCode,
                "Case-insensitive canonical match should NOT skip");
        }

        /// <summary>
        /// Separator normalization: forward vs backslash in canonical
        /// path should still match.
        /// </summary>
        [Test]
        public void SkipsNotification_ForwardSlashTempIndex()
        {
            int exitCode = RunHook("C:/repo/.git/tmp_idx", "C:\\repo\\.git");
            Assert.AreEqual(0, exitCode, "Forward-slash temp index should still be detected as non-canonical");
        }

        private int RunHook(string gitIndexFile, string gitDir)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = HookExePath,
                Arguments = "1 0",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,

                // Run outside any GVFS enlistment so the pipe lookup
                // fails predictably with NotInGVFSEnlistment.
                WorkingDirectory = Path.GetTempPath(),
            };

            // Set or remove env vars
            if (gitIndexFile != null)
            {
                psi.Environment["GIT_INDEX_FILE"] = gitIndexFile;
            }
            else
            {
                psi.Environment.Remove("GIT_INDEX_FILE");
            }

            if (gitDir != null)
            {
                psi.Environment["GIT_DIR"] = gitDir;
            }
            else
            {
                psi.Environment.Remove("GIT_DIR");
            }

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    process.Kill();
                    Assert.Fail("Hook process timed out (5s) — likely blocked on pipe connect");
                }

                return process.ExitCode;
            }
        }
    }
}
