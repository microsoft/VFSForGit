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
        private const uint NormalFile = 0x80;
        private const uint Directory = 0x10;
        private const string UnreadConfigErrors =
            "warning: unable to access '.git/config': Permission denied\n" +
            "warning: unable to access '.git/config': Permission denied\n" +
            "fatal: unknown error occurred while reading the configuration files\n";

        [TestCase("stash")]
        [TestCase("for-each-repo")]
        [TestCase("checkout")]
        [TestCase("update-index")]
        [TestCase("status")]
        public void BuiltInsDoNotQueryAliasesOrSearchPath(string command)
        {
            GitCommandResolver.TryIsAlias(
                command,
                () => throw new AssertionException("A built-in must not query aliases."),
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
                () => throw new AssertionException("An external path must not query aliases."),
                SearchPath,
                path => throw new AssertionException("An external path must not search PATH."),
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase("git-co", "alias.st\nalias.co\nalias.co\n")]
        [TestCase("git-CO", "alias.co\n")]
        [TestCase("git-myAlias", "alias.myalias\n")]
        [TestCase("git-sx", "alias.sx.command\n")]
        [TestCase("git-MiXeD", "alias.MiXeD.command\n")]
        [TestCase("git-a.b", "alias.a.b.command\n")]
        [TestCase("git-mixed.foo", "alias.MiXeD.foo\n")]
        [TestCase("git-CAF\u00e9.FOO", "alias.Caf\u00e9.foo\n")]
        [TestCase("git-caf\u00e9", "alias.caf\u00e9.command\n")]
        [TestCase("git-es", "alias..es\n")]
        [TestCase("git-ES", "alias..es\n")]
        [TestCase("git-command", "alias..command\n")]
        [TestCase("git-.review.foo", "alias..review.foo\n")]
        [TestCase("git-.REVIEW.FOO", "alias..review.foo\n")]
        [TestCase("git-.review", "alias..review.command\n")]
        [TestCase("git-git-prefixed", "alias.git-prefixed\n")]
        public void AliasKeysThatGitExpandsSkipTheOuterLock(string hookCommand, string aliasKeys)
        {
            int queries = 0;
            GitCommandResolver.TryIsAlias(
                hookCommand,
                () =>
                {
                    ++queries;
                    return new ProcessResult(aliasKeys, string.Empty, 0);
                },
                SearchPath,
                path => GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeTrue();

            queries.ShouldEqual(1);
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase("git-SX", "alias.sx.command\n", 0)]
        [TestCase("git-zzfoO", "alias.zzfoo.command\n", 0)]
        [TestCase("git-sx.command", "alias.sx.command\n", 0)]
        [TestCase("git-A.B", "alias.a.b.command\n", 0)]
        [TestCase("git-co", "alias.st\nalias.co-other\n", 0)]
        [TestCase("git-co", "alias.\n", 0)]
        [TestCase("git-", "alias.\nalias..\n", 0)]
        [TestCase("git-.command", "alias..command\n", 0)]
        [TestCase("git-review.foo", "alias..review.foo\n", 0)]
        [TestCase("git-.REVIEW", "alias..review.command\n", 0)]
        [TestCase("git-review", "alias..review.command\n", 0)]
        [TestCase("git-CAF\u00c9.FOO", "alias.Caf\u00e9.foo\n", 0)]
        [TestCase("git-co", "", 1)]
        public void AliasKeysThatGitDoesNotExpandKeepTheLock(string hookCommand, string aliasKeys, int exitCode)
        {
            GitCommandResolver.TryIsAlias(
                hookCommand,
                () => new ProcessResult(aliasKeys, "13:00:00.000000 git.c:1 trace: built-in: git config\n", exitCode),
                SearchPath,
                path => GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase("git-co", @"mock:\git\libexec\git-co.exe")]
        [TestCase("git-co", @"mock:\tools\git-co")]
        [TestCase("git-Co", @"mock:\tools\GIT-CO.EXE")]
        [TestCase("git-co.EXE", @"mock:\tools\git-co.EXE")]
        [TestCase("git-git-prefixed", @"mock:\tools\git-git-prefixed")]
        public void ProgramsTakePrecedenceWithoutAnAliasQuery(string hookCommand, string programPath)
        {
            GitCommandResolver.TryIsAlias(
                hookCommand,
                () => throw new AssertionException("A program must not query aliases."),
                SearchPath,
                path => string.Equals(path, programPath, StringComparison.OrdinalIgnoreCase) ? NormalFile : GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase(@"mock:\tools\git-co.exe", false)]
        [TestCase(@"mock:\tools\git-co", true)]
        public void DirectoriesMatchOnlyTheExeCandidate(string directoryPath, bool isAliasExpected)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => new ProcessResult("alias.co\n", string.Empty, 0),
                @"mock:\tools",
                path => string.Equals(path, directoryPath, StringComparison.Ordinal) ? Directory : GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldEqual(isAliasExpected);
            error.ShouldBeNull();
        }

        [TestCase(128)]
        [TestCase(1)]
        public void UnreadConfigIsAFailure(int exitCode)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => new ProcessResult(string.Empty, UnreadConfigErrors, exitCode),
                SearchPath,
                path => GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("Unable to read Git aliases (exit code " + exitCode + ")", "unable to access '.git/config'");
        }

        [TestCase(128, "")]
        [TestCase(128, "alias.co\n")]
        [TestCase(1, "alias.co\n")]
        public void FailedAliasQueriesNeverGrantAnAliasExemption(int exitCode, string output)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => new ProcessResult(output, "config read failed", exitCode),
                SearchPath,
                path => GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("Unable to read Git aliases", "exit code " + exitCode, "config read failed");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void AliasQueryFailuresProducePreciseDiagnostics()
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
                GitCommandResolver.TryIsAlias(
                    "git-co",
                    () => throw failure,
                    SearchPath,
                    path => GitCommandResolver.InvalidFileAttributes,
                    out bool isAlias,
                    out string error).ShouldBeFalse();

                isAlias.ShouldBeFalse();
                error.ShouldContain("Unable to read Git aliases", failure.GetType().Name, failure.HResult.ToString("X8"), failure.Message);
                error.Contains(" at GVFS.").ShouldBeFalse();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void MissingParentProcessIsNamedInTheError()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => GitCommandResolver.GetProcessPath(-1));
            failure.Message.ShouldContain("parent Git process (PID -1)");
        }

        [TestCase(null)]
        [TestCase("")]
        public void MissingSearchPathIsAFailure(string searchPath)
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => throw new AssertionException("An empty PATH must not query aliases."),
                searchPath,
                path => throw new AssertionException("An empty PATH must not be searched."),
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("inherited PATH is empty");
        }

        [TestCase]
        public void SearchPathUsesRawEntriesAndSkipsEmptyEntries()
        {
            List<string> candidates = new List<string>();
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => new ProcessResult("alias.co\n", string.Empty, 0),
                @"; spaced ;relative tools;;mock:\tools\;",
                path =>
                {
                    candidates.Add(path);
                    return GitCommandResolver.InvalidFileAttributes;
                },
                out bool isAlias,
                out string error).ShouldBeTrue();

            candidates.ShouldMatchInOrder(
                @" spaced \git-co.exe",
                @" spaced \git-co",
                @"relative tools\git-co.exe",
                @"relative tools\git-co",
                @"mock:\tools\\git-co.exe",
                @"mock:\tools\\git-co");
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase]
        public void CandidatesThatFitGitsPathBufferAreChecked()
        {
            // Git builds "<entry>\<command>.exe" in a 260-byte buffer. 259 bytes and the NUL fit.
            string directory = @"mock:\" + new string('d', 242);
            List<string> candidates = new List<string>();
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => new ProcessResult("alias.co\n", string.Empty, 0),
                directory,
                path =>
                {
                    candidates.Add(path);
                    return GitCommandResolver.InvalidFileAttributes;
                },
                out bool isAlias,
                out string error).ShouldBeTrue();

            candidates.ShouldMatchInOrder(directory + @"\git-co.exe", directory + @"\git-co");
            isAlias.ShouldBeTrue();
        }

        [TestCase(243, "")]
        [TestCase(129, "\u00e9")]
        public void CandidatesLongerThanGitsPathBufferAreFailures(int directoryLength, string filler)
        {
            string directory = @"mock:\" + (filler.Length == 0 ? new string('d', directoryLength) : string.Concat(System.Linq.Enumerable.Repeat(filler, directoryLength)));
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => throw new AssertionException("A failed PATH search must not query aliases."),
                @"mock:\tools-without-co;" + directory + @";mock:\tools",
                path => path.StartsWith(@"mock:\tools\", StringComparison.Ordinal) ? NormalFile : GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeFalse();

            isAlias.ShouldBeFalse();
            error.ShouldContain("is too long for Git's path buffer");
        }

        [TestCase]
        public void ProgramsFoundBeforeALongEntryDoNotFail()
        {
            GitCommandResolver.TryIsAlias(
                "git-co",
                () => throw new AssertionException("A program must not query aliases."),
                @"mock:\tools;mock:\" + new string('d', 300),
                path => path == @"mock:\tools\git-co.exe" ? NormalFile : GitCommandResolver.InvalidFileAttributes,
                out bool isAlias,
                out string error).ShouldBeTrue();

            isAlias.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase]
        public void ExecutableSuffixDoesNotAddASecondExtension()
        {
            List<string> candidates = new List<string>();
            GitCommandResolver.TryIsAlias(
                "git-co.EXE",
                () => new ProcessResult("alias.co.exe\n", string.Empty, 0),
                @"mock:\tools",
                path =>
                {
                    candidates.Add(path);
                    return GitCommandResolver.InvalidFileAttributes;
                },
                out bool isAlias,
                out string error).ShouldBeTrue();

            candidates.ShouldMatchInOrder(@"mock:\tools\git-co.EXE");
            isAlias.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [TestCase]
        public void GetFileAttributesFailsForPathsThatGitCannotAccess()
        {
            string directory = Path.Combine(Path.GetTempPath(), "GitCommandResolverTests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                string file = Path.Combine(directory, "git-co");
                File.WriteAllText(file, string.Empty);

                (GitCommandResolver.GetFileAttributes(file) & Directory).ShouldEqual(0u);
                (GitCommandResolver.GetFileAttributes(directory) & Directory).ShouldEqual(Directory);
                GitCommandResolver.GetFileAttributes(Path.Combine(directory, "missing", "git-co")).ShouldEqual(GitCommandResolver.InvalidFileAttributes);
                GitCommandResolver.GetFileAttributes("\"" + directory + "\"\\git-co").ShouldEqual(GitCommandResolver.InvalidFileAttributes);
            }
            finally
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase]
        public void CurrentProcessPathUsesTheSuppliedPid()
        {
            GitCommandResolver.GetProcessPath(Environment.ProcessId).ShouldEqual(Environment.ProcessPath);
        }

        [TestCase]
        public void AliasKeyQueryUsesTheParentGit()
        {
            const string ParentGit = @"mock:\custom git\bin\git.exe";
            ProcessStartInfo query = GitCommandResolver.CreateAliasKeyQuery(ParentGit);

            query.FileName.ShouldEqual(ParentGit);
            query.UseShellExecute.ShouldBeFalse();
            query.RedirectStandardOutput.ShouldBeTrue();
            query.RedirectStandardError.ShouldBeTrue();
            query.ArgumentList.ShouldMatchInOrder("config", "--name-only", "--get-regexp", @"^alias\.");
            query.StandardOutputEncoding.WebName.ShouldEqual("utf-8");
            query.Environment["COMMAND_HOOK_LOCK"].ShouldEqual("true");
        }

        [TestCase(null)]
        [TestCase("fr_FR.UTF-8")]
        [NonParallelizable]
        public void AliasKeyQueryUsesCLocaleWithoutChangingParentEnvironment(string inheritedLocale)
        {
            string originalLocale = Environment.GetEnvironmentVariable("LC_ALL");
            string originalLanguage = Environment.GetEnvironmentVariable("LANGUAGE");
            try
            {
                Environment.SetEnvironmentVariable("LC_ALL", inheritedLocale);
                Environment.SetEnvironmentVariable("LANGUAGE", "de");
                ProcessStartInfo inherited = new ProcessStartInfo();
                ProcessStartInfo query = GitCommandResolver.CreateAliasKeyQuery(@"mock:\git.exe");

                query.Environment["LC_ALL"].ShouldEqual("C");
                foreach (KeyValuePair<string, string> variable in inherited.Environment)
                {
                    if (!string.Equals(variable.Key, "LC_ALL", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(variable.Key, "COMMAND_HOOK_LOCK", StringComparison.OrdinalIgnoreCase))
                    {
                        query.Environment[variable.Key].ShouldEqual(variable.Value, variable.Key);
                    }
                }

                Environment.GetEnvironmentVariable("LC_ALL").ShouldEqual(inheritedLocale);
                Environment.GetEnvironmentVariable("LANGUAGE").ShouldEqual("de");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LC_ALL", originalLocale);
                Environment.SetEnvironmentVariable("LANGUAGE", originalLanguage);
            }
        }

        [TestCase]
        public void ConfigBoolQueryUsesTheParentGit()
        {
            const string ParentGit = @"mock:\custom git\bin\git.exe";
            ProcessStartInfo query = GitCommandResolver.CreateConfigBoolQuery(ParentGit, "gvfs.show-hydration-status");

            query.FileName.ShouldEqual(ParentGit);
            query.UseShellExecute.ShouldBeFalse();
            query.RedirectStandardInput.ShouldBeTrue();
            query.RedirectStandardOutput.ShouldBeTrue();
            query.RedirectStandardError.ShouldBeTrue();
            query.ArgumentList.ShouldMatchInOrder("config", "--type=bool", "gvfs.show-hydration-status");
            query.Environment["COMMAND_HOOK_LOCK"].ShouldEqual("true");
            query.Environment["LC_ALL"].ShouldEqual("C");
        }
    }
}
