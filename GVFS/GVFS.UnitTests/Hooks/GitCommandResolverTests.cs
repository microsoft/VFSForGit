using GVFS.Common;
using GVFS.Hooks;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class GitCommandResolverTests
    {
        private const string SearchPath = @"mock:\git\libexec;mock:\tools";

        [TestCase("stash")]
        [TestCase("for-each-repo")]
        [TestCase("checkout")]
        [TestCase("update-index")]
        [TestCase("status")]
        public void BuiltInsDoNotQueryAliasesOrSearchPath(string command)
        {
            GitCommandResolver.TryIsAlias(
                command,
                key => throw new AssertionException("A built-in must not query aliases."),
                SearchPath,
                path => throw new AssertionException("A built-in must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase("git-tools/command")]
        [TestCase(@"git-tools\command")]
        public void ExternalPathsDoNotQueryAliases(string command)
        {
            GitCommandResolver.TryIsAlias(
                command,
                key => throw new AssertionException("An external path must not query aliases."),
                SearchPath,
                path => throw new AssertionException("An external path must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase("git-co", "checkout")]
        [TestCase("git-shell-alias", "!git status")]
        [TestCase("git-first", "second")]
        [TestCase("git-second", "status")]
        [TestCase("git-MiXeD", "status")]
        [TestCase("git-git-prefixed", "status")]
        public void GenuineAliasesSkipOnlyTheOuterLock(string hookCommand, string alias)
        {
            int queries = 0;
            GitCommandResolver.TryIsAlias(
                hookCommand,
                key =>
                {
                    key.ShouldEqual("alias." + hookCommand.Substring(4));
                    ++queries;
                    return new ProcessResult(alias + "\n", string.Empty, 0);
                },
                SearchPath,
                path => false,
                out bool isAlias,
                out string error).ShouldBeTrue();

            queries.ShouldEqual(1);
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase("git-co", @"mock:\git\libexec\git-co.exe")]
        [TestCase("git-co", @"mock:\tools\git-co")]
        [TestCase("git-Co", @"mock:\tools\GIT-CO.EXE")]
        [TestCase("git-co.EXE", @"mock:\tools\git-co.exe")]
        [TestCase("git-git-prefixed", @"mock:\tools\git-git-prefixed")]
        public void RealExternalsTakePrecedenceOverAliases(string hookCommand, string executablePath)
        {
            GitCommandResolver.TryIsAlias(
                hookCommand,
                key =>
                {
                    key.ShouldEqual("alias." + hookCommand.Substring(4));
                    return new ProcessResult("!git update-index --refresh\n", string.Empty, 0);
                },
                SearchPath,
                path => string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase(1, "")]
        [TestCase(0, "")]
        [TestCase(0, "\n")]
        public void MissingOrEmptyAliasesKeepTheLockWithoutSearchingPath(int exitCode, string output)
        {
            GitCommandResolver.TryIsAlias(
                "git-unknown-command",
                key => new ProcessResult(output, string.Empty, exitCode),
                SearchPath,
                path => throw new AssertionException("A command without an alias must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase]
        public void MissingAliasWithTraceOutputStillKeepsTheLock()
        {
            GitCommandResolver.TryIsAlias(
                "git-unknown-command",
                key => new ProcessResult(string.Empty, "trace: built-in: git config --get alias.unknown-command", 1),
                SearchPath,
                path => throw new AssertionException("A missing alias must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase(128, "")]
        [TestCase(128, "status\n")]
        [TestCase(1, "status\n")]
        public void FailedConfigReadsNeverGrantAnAliasExemption(int exitCode, string output)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                key => new ProcessResult(output, "config read failed", exitCode),
                SearchPath,
                path => throw new AssertionException("A failed config read must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("Unable to read Git alias 'co'");
            error.ShouldContain("exit code " + exitCode);
            error.ShouldContain("config read failed");
        }

        [TestCase(null)]
        [TestCase("")]
        public void MissingSearchPathProducesADiagnostic(string searchPath)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                key => new ProcessResult("checkout", string.Empty, 0),
                searchPath,
                path => false,
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("inherited PATH is empty");
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FileLookupFailuresProduceADiagnostic(bool accessDenied)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                key => new ProcessResult("checkout", string.Empty, 0),
                SearchPath,
                path =>
                {
                    if (accessDenied)
                    {
                        throw new UnauthorizedAccessException("access denied");
                    }

                    throw new IOException("lookup failed");
                },
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("Unable to resolve Git command 'co'");
            error.ShouldContain(accessDenied ? "access denied" : "lookup failed");
            error.ShouldContain(@"mock:\git\libexec\git-co.exe");
            error.ShouldContain("0x");
            error.ShouldContain(accessDenied ? "UnauthorizedAccessException" : "IOException");
        }

        [TestCase]
        public void SearchPathUsesRelativeDirectoriesAndSkipsEmptyEntries()
        {
            List<string> candidates = new List<string>();
            GitCommandResolver.TryIsAlias(
                "git-co",
                key => new ProcessResult("checkout", string.Empty, 0),
                @";relative tools;;mock:\tools;",
                path =>
                {
                    candidates.Add(path);
                    return false;
                },
                out bool isAlias,
                out string error).ShouldBeTrue();

            candidates.ShouldMatchInOrder(
                Path.Combine("relative tools", "git-co.exe"),
                Path.Combine("relative tools", "git-co"),
                @"mock:\tools\git-co.exe",
                @"mock:\tools\git-co");
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase]
        public void ExecutableChangesDoNotUseAStaleCommandCache()
        {
            bool executableExists = false;
            foreach (bool exists in new[] { false, true, false })
            {
                executableExists = exists;
                GitCommandResolver.TryIsAlias(
                    "git-co",
                    key => new ProcessResult("checkout", string.Empty, 0),
                    SearchPath,
                    path => executableExists,
                    out bool isAlias,
                    out string error).ShouldBeTrue();

                isAlias.ShouldEqual(!exists);
                error.ShouldBeNull();
            }
        }

        [TestCase]
        public void ExecutableSuffixDoesNotAddASecondExtension()
        {
            List<string> candidates = new List<string>();
            GitCommandResolver.TryIsAlias(
                "git-co.EXE",
                key => new ProcessResult("status", string.Empty, 0),
                @"mock:\tools",
                path =>
                {
                    candidates.Add(path);
                    return false;
                },
                out bool isAlias,
                out string error).ShouldBeTrue();

            candidates.ShouldMatchInOrder(@"mock:\tools\git-co.EXE");
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase]
        public void CurrentProcessPathUsesTheSuppliedPid()
        {
            GitCommandResolver.GetProcessPath(Environment.ProcessId).ShouldEqual(Environment.ProcessPath);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void InvalidParentPidProducesAQueryFailure()
        {
            string diagnostic = null;
            GitCommandResolver.ShouldLock(
                "git-co",
                key =>
                {
                    GitCommandResolver.GetProcessPath(-1);
                    throw new AssertionException("An invalid process ID must not return an executable path.");
                },
                SearchPath,
                path => throw new AssertionException("An invalid parent must not search PATH."),
                message => diagnostic = message).ShouldBeTrue();

            diagnostic.ShouldContain("ArgumentException");
            diagnostic.ShouldContain("GVFS will keep the command lock.");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void QueryFailuresKeepTheLockWithPreciseDiagnostics()
        {
            foreach (Exception failure in new Exception[]
            {
                new Win32Exception(5),
                new InvalidOperationException("No executable path"),
                new ArgumentException("No parent process"),
                new IOException("Read failed"),
                new UnauthorizedAccessException("Access denied"),
            })
            {
                string diagnostic = null;
                GitCommandResolver.ShouldLock(
                    "git-co",
                    key => throw failure,
                    SearchPath,
                    path => throw new AssertionException("A failed query must not search PATH."),
                    message => diagnostic = message).ShouldBeTrue();

                diagnostic.ShouldContain("Unable to query Git alias 'co'");
                diagnostic.ShouldContain(failure.GetType().Name);
                diagnostic.ShouldContain(failure.HResult.ToString("X8"));
                diagnostic.ShouldContain(failure.Message);
                diagnostic.ShouldContain("GVFS will keep the command lock.");
                diagnostic.Contains(" at GVFS.").ShouldBeFalse();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PathProbeFailuresKeepTheLockInsteadOfAbortingAliases()
        {
            foreach (Exception failure in new Exception[]
            {
                new IOException("Unavailable drive"),
                new PathTooLongException("Path too long"),
                new UnauthorizedAccessException("Access denied"),
                new ArgumentException("Invalid path"),
            })
            {
                string diagnostic = null;
                GitCommandResolver.ShouldLock(
                    "git-co",
                    key => new ProcessResult("checkout", string.Empty, 0),
                    SearchPath,
                    path => throw failure,
                    message => diagnostic = message).ShouldBeTrue();

                diagnostic.ShouldContain(@"mock:\git\libexec\git-co.exe");
                diagnostic.ShouldContain(failure.GetType().Name);
                diagnostic.ShouldContain("GVFS will keep the command lock.");
            }
        }

        [TestCase]
        public void FailedConfigReadKeepsTheLockWithItsExitCode()
        {
            string diagnostic = null;
            GitCommandResolver.ShouldLock(
                "git-co",
                key => new ProcessResult("checkout", "config read failed", 128),
                SearchPath,
                path => throw new AssertionException("A failed query must not search PATH."),
                message => diagnostic = message).ShouldBeTrue();

            diagnostic.ShouldContain("exit code 128");
            diagnostic.ShouldContain("config read failed");
            diagnostic.ShouldContain("GVFS will keep the command lock.");
        }

        [TestCase]
        public void GenuineAliasExemptionDoesNotReportAnError()
        {
            GitCommandResolver.ShouldLock(
                "git-co",
                key => new ProcessResult("checkout", string.Empty, 0),
                SearchPath,
                path => false,
                message => throw new AssertionException(message)).ShouldBeFalse();
        }

        [TestCase]
        public void AliasQueryUsesTheParentGitAndSuppressesRecursiveHooks()
        {
            const string ParentGit = @"mock:\custom git\bin\git.exe";
            const string AliasKey = "alias.git-MiXeD";
            ProcessStartInfo query = GitCommandResolver.CreateAliasQuery(ParentGit, AliasKey);

            query.FileName.ShouldEqual(ParentGit);
            query.UseShellExecute.ShouldBeFalse();
            query.RedirectStandardInput.ShouldBeTrue();
            query.RedirectStandardOutput.ShouldBeTrue();
            query.RedirectStandardError.ShouldBeTrue();
            query.ArgumentList.ShouldMatchInOrder("config", "--get", AliasKey);
            query.Environment["COMMAND_HOOK_LOCK"].ShouldEqual("true");
            query.Environment["PATH"].ShouldEqual(Environment.GetEnvironmentVariable("PATH"));
            query.Environment.TryGetValue("GIT_EXEC_PATH", out string execPath);
            execPath.ShouldEqual(Environment.GetEnvironmentVariable("GIT_EXEC_PATH"));
            query.Environment.TryGetValue("GIT_CONFIG_PARAMETERS", out string parameters);
            parameters.ShouldEqual(Environment.GetEnvironmentVariable("GIT_CONFIG_PARAMETERS"));
        }
    }
}
