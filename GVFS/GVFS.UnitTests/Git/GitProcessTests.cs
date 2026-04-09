using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Diagnostics;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitProcessTests
    {
        [TestCase]
        public void TryKillRunningProcess_NeverRan()
        {
            GitProcess process = new GitProcess(new MockGVFSEnlistment());
            process.TryKillRunningProcess(out string processName, out int exitCode, out string error).ShouldBeTrue();

            processName.ShouldBeNull();
            exitCode.ShouldEqual(-1);
            error.ShouldBeNull();
        }

        [TestCase]
        public void ResultHasNoErrors()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                string.Empty,
                0);

            result.ExitCodeIsFailure.ShouldBeFalse();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ResultHasWarnings()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                "Warning: this is fine.\n",
                0);

            result.ExitCodeIsFailure.ShouldBeFalse();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_SingleLine_AllWarnings()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                "warning: this line should not be considered an error",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_Multiline_AllWarnings()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                @"warning: this line should not be considered an error
WARNING: neither should this.",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_Multiline_EmptyLines()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                @"
warning: this is fine

warning: this is too

",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_Singleline_AllErrors()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                "this is an error",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeTrue();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_Multiline_AllErrors()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                @"error1
error2",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeTrue();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_Multiline_ErrorsAndWarnings()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                @"WARNING: this is fine
this is an error",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeTrue();
        }

        [TestCase]
        public void ResultHasNonWarningErrors_TrailingWhitespace_Warning()
        {
            GitProcess.Result result = new GitProcess.Result(
                string.Empty,
                "Warning: this is fine\n",
                1);

            result.ExitCodeIsFailure.ShouldBeTrue();
            result.StderrContainsErrors().ShouldBeFalse();
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_DefaultIsNull()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, string.Empty, 1),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string _).ShouldBeTrue();
            expectedValue.ShouldBeNull();
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_FailsWhenErrors()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, "errors", 1),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_NullWhenUnsetAndWarnings()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, "warning: ignored", 1),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string _).ShouldBeTrue();
            expectedValue.ShouldBeNull();
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_PassesThroughErrors()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, "--local can only be used inside a git repository", 1),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string error).ShouldBeFalse();
            error.Contains("--local").ShouldBeTrue();
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_RespectsDefaultOnFailure()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, string.Empty, 1),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string _, "default").ShouldBeTrue();
            expectedValue.ShouldEqual("default");
        }

        [TestCase]
        public void ConfigResult_TryParseAsString_OverridesDefaultOnSuccess()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result("expected", string.Empty, 0),
                "settingName");

            result.TryParseAsString(out string expectedValue, out string _, "default").ShouldBeTrue();
            expectedValue.ShouldEqual("expected");
        }

        [TestCase]
        public void ConfigResult_TryParseAsInt_FailsWithErrors()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, "errors", 1),
                "settingName");

            result.TryParseAsInt(0, -1, out int value, out string error).ShouldBeFalse();
        }

        [TestCase]
        public void ConfigResult_TryParseAsInt_DefaultWhenUnset()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result(string.Empty, string.Empty, 1),
                "settingName");

            result.TryParseAsInt(1, -1, out int value, out string error).ShouldBeTrue();
            value.ShouldEqual(1);
        }

        [TestCase]
        public void ConfigResult_TryParseAsInt_ParsesWhenNoError()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result("32", string.Empty, 0),
                "settingName");

            result.TryParseAsInt(1, -1, out int value, out string error).ShouldBeTrue();
            value.ShouldEqual(32);
        }

        [TestCase]
        public void ConfigResult_TryParseAsInt_ParsesWhenWarnings()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result("32", "warning: ignored", 0),
                "settingName");

            result.TryParseAsInt(1, -1, out int value, out string error).ShouldBeTrue();
            value.ShouldEqual(32);
        }

        [TestCase]
        public void ConfigResult_TryParseAsInt_ParsesWhenOutputIncludesWhitespace()
        {
            GitProcess.ConfigResult result = new GitProcess.ConfigResult(
                new GitProcess.Result("\n\t 32\t\r\n", "warning: ignored", 0),
                "settingName");

            result.TryParseAsInt(1, -1, out int value, out string error).ShouldBeTrue();
            value.ShouldEqual(32);
        }

        [TestCase("dir/file.txt", "\"dir/file.txt\"")]
        [TestCase("my dir/my file.txt", "\"my dir/my file.txt\"")]
        [TestCase("dir/file\"name.txt", "\"dir/file\\\"name.txt\"")]
        [TestCase("\"quoted\"", "\"\\\"quoted\\\"\"")]
        [TestCase("dir\\subdir\\file.txt", "\"dir\\subdir\\file.txt\"")] // Backslashes as path separators left as-is
        [TestCase("", "\"\"")]
        [TestCase("dir\\\"file.txt", "\"dir\\\\\\\"file.txt\"")] // Backslash before quote: doubled, then quote escaped
        [TestCase("dir\\subdir\\", "\"dir\\subdir\\\\\"")] // Trailing backslash doubled
        public void QuoteGitPath(string input, string expected)
        {
            GitProcess.QuoteGitPath(input).ShouldEqual(expected);
        }

        [TestCase]
        [Description("Integration test: verify QuoteGitPath produces arguments that git actually receives correctly")]
        public void QuoteGitPath_RoundTripThroughProcess()
        {
            // Test that paths with special characters survive the
            // ProcessStartInfo.Arguments → Windows CRT argument parsing → git round-trip.
            // We use "git rev-parse --sq-quote <path>" which echoes the path back
            // in shell-quoted form, proving git received it correctly.
            string[] testPaths = new[]
            {
                "simple/path.txt",
                "path with spaces/file name.txt",
                "path\\with\\backslashes\\file.txt",
            };

            string gitPath = "C:\\Program Files\\Git\\cmd\\git.exe";
            if (!System.IO.File.Exists(gitPath))
            {
                Assert.Ignore("Git not found at expected path — skipping integration test");
            }

            foreach (string testPath in testPaths)
            {
                string quoted = GitProcess.QuoteGitPath(testPath);
                ProcessStartInfo psi = new ProcessStartInfo(gitPath)
                {
                    Arguments = "rev-parse --sq-quote " + quoted,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();

                    // git sq-quote wraps in single quotes and escapes single quotes
                    // For a simple path "foo/bar.txt" → output is "'foo/bar.txt'"
                    // Strip the outer single quotes to get the raw path back
                    if (output.StartsWith("'") && output.EndsWith("'"))
                    {
                        output = output.Substring(1, output.Length - 2);
                    }

                    output.ShouldEqual(
                        testPath,
                        $"Path round-trip failed for: {testPath} (quoted as: {quoted})");
                }
            }
        }
    }
}
