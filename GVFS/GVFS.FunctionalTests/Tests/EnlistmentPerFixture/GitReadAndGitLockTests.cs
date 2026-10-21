using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class GitReadAndGitLockTests : TestsWithEnlistmentPerFixture
    {
        private const string ExpectedStatusWaitingText = @"Waiting for 'GVFS.FunctionalTests.LockHolder'";
        private const int AcquireGVFSLockTimeout = 10 * 1000;
        private FileSystemRunner fileSystem;

        public GitReadAndGitLockTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase, Order(1)]
        public void GitStatus()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "nothing to commit, working tree clean");
        }

        [TestCase, Order(2)]
        public void GitLog()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "log -n1", "commit", "Author:", "Date:");
        }

        [TestCase, Order(3)]
        public void GitBranch()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "branch -a",
                "* " + Properties.Settings.Default.Commitish,
                "remotes/origin/" + Properties.Settings.Default.Commitish);
        }

        [TestCase, Order(4)]
        public void GitCommandWaitsWhileAnotherIsRunning()
        {
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 3000);

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(5)]
        public void GitAliasNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasNamedAfterKnownCommandAcquiresLock);

            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: AcquireGVFSLockTimeout);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " status");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, alias, removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(6)]
        public void GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock()
        {
            string alias = nameof(this.GitAliasInSubfolderNamedAfterKnownCommandAcquiresLock);

            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: AcquireGVFSLockTimeout);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + alias + " rebase");
            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(
                Path.Combine(this.Enlistment.RepoRoot, "GVFS"),
                alias + " origin/FunctionalTests/RebaseTestsSource_20170208",
                removeWaitingMessages: false);
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "rebase --abort");
        }

        [TestCase, Order(7)]
        public void ExternalLockHolderReportedWhenBackgroundTasksArePending()
        {
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 3000);

            // Creating a new file will queue a background task
            string newFilePath = this.Enlistment.GetVirtualPathTo("ExternalLockHolderReportedWhenBackgroundTasksArePending.txt");
            newFilePath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(newFilePath, "New file contents");

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);

            // Validate that GVFS still reports that the git command is holding the lock
            statusWait.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase, Order(8)]
        public void OrphanedGVFSLockIsCleanedUp()
        {
            int pid;
            GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 1000, skipReleaseLock: true);

            while (true)
            {
                try
                {
                    using (Process.GetProcessById(pid))
                    {
                    }

                    Thread.Sleep(1000);
                }
                catch (ArgumentException)
                {
                    break;
                }
            }

            ProcessResult statusWait = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status", removeWaitingMessages: false);

            // There should not be any errors - in particular, there should not be
            // an error about "Waiting for GVFS.FunctionalTests.LockHolder"
            statusWait.Errors.ShouldEqual(string.Empty);
        }

        [TestCase("stash", "list")]
        [TestCase("stash.exe", "list")]
        [TestCase("for-each-repo", "--config=gvfs.tests.alias-repositories status --porcelain")]
        [TestCase("checkout", "--detach HEAD")]
        [Order(9)]
        public void BuiltInWithSameNameAliasStillAcquiresLock(string command, string arguments)
        {
            string aliasName = command.EndsWith(".exe", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(command) : command;
            string repositoryConfig = command == "for-each-repo"
                ? "-c \"gvfs.tests.alias-repositories=" + this.Enlistment.RepoRoot + "\" "
                : string.Empty;
            ProcessResult result = this.RunWithLockHolder(repositoryConfig + "-c alias." + aliasName + "=version " + command + " " + arguments);
            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase("gvfs-lock-outer-alias", "status --porcelain")]
        [TestCase("gvfs-lock-outer-alias", "!git status --porcelain")]
        [TestCase("gvfs-lock-outer-alias", "gvfs-lock-inner-alias")]
        [TestCase("MiXeD-lock-alias", "status --porcelain")]
        [TestCase("git-prefixed-lock-alias", "status --porcelain")]
        [Order(10)]
        public void AliasInnerCommandStillAcquiresLock(string aliasName, string expansion)
        {
            string command = "-c alias.gvfs-lock-inner-alias=status " +
                "-c \"alias." + aliasName + "=" + expansion + "\" " + aliasName;
            ProcessResult result = this.RunWithLockHolder(command);
            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase]
        [Order(11)]
        public void ReadOnlyAliasDoesNotAcquireAnOuterLock()
        {
            ProcessResult result = this.RunWithLockHolder(
                "-c \"alias.gvfs-read-only-alias=!git rev-parse --show-toplevel\" gvfs-read-only-alias",
                shouldWait: false);
            result.ExitCode.ShouldEqual(0);
            result.Output.ShouldContain(this.Enlistment.RepoRoot.Replace('\\', '/'));
            result.Errors.Contains(ExpectedStatusWaitingText).ShouldBeFalse();
        }

        [TestCase]
        [Order(12)]
        public void UnknownExternalCommandStillAcquiresLock()
        {
            ProcessResult result = this.RunWithLockHolder("-c help.autocorrect=never gvfs-unknown-lock-command");
            result.ExitCode.ShouldEqual(1);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase("git-", false, false)]
        [TestCase("GIT-", true, false)]
        [TestCase("git-", false, true)]
        [Order(13)]
        public void ExternalWithSameNameAliasStillAcquiresLock(string prefix, bool uppercaseCommand, bool useExecPath)
        {
            const string CommandName = "gvfs-lock-external";
            string directory = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.ExternalWithSameNameAliasStillAcquiresLock));
            Directory.CreateDirectory(directory);
            string scriptPath = Path.Combine(directory, prefix + CommandName);
            File.WriteAllText(scriptPath, "#!/bin/sh\nexec git status --porcelain\n");
            try
            {
                Dictionary<string, string> environment = new Dictionary<string, string>();
                if (useExecPath)
                {
                    environment["GIT_EXEC_PATH"] = directory;
                }
                else
                {
                    environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                }

                string invocation = uppercaseCommand ? CommandName.ToUpperInvariant() : CommandName;
                ProcessResult result = this.RunWithLockHolder("-c alias." + CommandName + "=version " + invocation, environment);
                result.ExitCode.ShouldEqual(0);
                result.Errors.ShouldContain(ExpectedStatusWaitingText);
            }
            finally
            {
                File.Delete(scriptPath);
                Directory.Delete(directory);
            }
        }

        [TestCase]
        [Order(14)]
        public void ExternalExecutableWithSameNameAliasStillAcquiresLock()
        {
            const string CommandName = "gvfs-lock-executable";
            string directory = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.ExternalExecutableWithSameNameAliasStillAcquiresLock));
            Directory.CreateDirectory(directory);
            string executablePath = Path.Combine(directory, "GIT-" + CommandName + ".EXE");
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executablePath);
            try
            {
                Dictionary<string, string> environment = new Dictionary<string, string>();
                environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                ProcessResult result = this.RunWithLockHolder("-c alias." + CommandName + "=version " + CommandName + " /d /c echo external-command", environment);
                result.ExitCode.ShouldEqual(0);
                result.Output.ShouldContain("external-command");
                result.Errors.ShouldContain(ExpectedStatusWaitingText);
            }
            finally
            {
                File.Delete(executablePath);
                Directory.Delete(directory);
            }
        }

        [TestCase("")]
        [Order(15)]
        public void DirectoryNamedAfterAnExternalDoesNotHideAnAlias(string suffix)
        {
            const string CommandName = "gvfs-directory-alias";
            string directory = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.DirectoryNamedAfterAnExternalDoesNotHideAnAlias));
            string commandDirectory = Path.Combine(directory, "git-" + CommandName + suffix);
            Directory.CreateDirectory(commandDirectory);
            try
            {
                Dictionary<string, string> environment = new Dictionary<string, string>();
                environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                ProcessResult result = this.RunWithLockHolder(
                    "-c alias." + CommandName + "=version " + CommandName,
                    environment,
                    shouldWait: false);
                result.ExitCode.ShouldEqual(0);
                result.Output.ShouldContain("git version");
            }
            finally
            {
                Directory.Delete(commandDirectory);
                Directory.Delete(directory);
            }
        }

        [TestCase]
        [Order(16)]
        public void PostCommandReleasesOwnedLockAfterExternalDisappears()
        {
            const string CommandName = "gvfs-lock-race";
            string directory = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.PostCommandReleasesOwnedLockAfterExternalDisappears));
            Directory.CreateDirectory(directory);
            string executablePath = Path.Combine(directory, "git-" + CommandName);
            File.WriteAllText(executablePath, "#!/bin/sh\nexit 99\n");
            string hooksExecutable = Path.Combine(Path.GetDirectoryName(Properties.Settings.Default.PathToGVFS), "GVFS.Hooks.exe");
            Dictionary<string, string> environment = new Dictionary<string, string>();
            environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = "alias." + CommandName;
            environment["GIT_CONFIG_VALUE_0"] = "checkout --detach HEAD";

            // Keep the Git PID alive so orphan cleanup cannot hide a skipped release.
            using (Process git = new Process())
            {
                git.StartInfo = this.CreateGitStartInfo("hash-object --stdin", environment);
                git.StartInfo.RedirectStandardInput = true;
                bool started = false;
                try
                {
                    started = git.Start();
                    started.ShouldBeTrue();
                    string commandArguments = " git-" + CommandName + " --git-pid=" + git.Id;
                    ProcessStartInfo hook = this.CreateGitStartInfo("pre-command" + commandArguments, environment);
                    hook.FileName = hooksExecutable;
                    ProcessResult preCommand = ProcessHelper.Run(hook, timeoutMs: 30000);
                    preCommand.ExitCode.ShouldEqual(0, preCommand.Output + preCommand.Errors);
                    this.Enlistment.Status().ShouldContain("GVFS Lock: Held by git git-" + CommandName + " (PID:" + git.Id + ")");

                    File.Delete(executablePath);
                    hook.Arguments = "post-command" + commandArguments;
                    ProcessResult postCommand = ProcessHelper.Run(hook, timeoutMs: 30000);
                    postCommand.ExitCode.ShouldEqual(0, postCommand.Output + postCommand.Errors);
                    git.HasExited.ShouldBeFalse();
                    this.Enlistment.Status().ShouldContain("GVFS Lock: Free");
                }
                finally
                {
                    if (started)
                    {
                        git.StandardInput.Close();
                        if (!git.WaitForExit(10000))
                        {
                            git.Kill(entireProcessTree: true);
                            git.WaitForExit();
                            Assert.Fail("The parent Git process did not exit.");
                        }
                    }

                    File.Delete(executablePath);
                    Directory.Delete(directory);
                }
            }
        }

        [TestCase("\"C:\\invalid quoted path\"")]
        [TestCase("C:\\invalid|path")]
        [Order(17)]
        public void InvalidPathEntryDoesNotPreventAGenuineAlias(string pathEntry)
        {
            Dictionary<string, string> environment = new Dictionary<string, string>();
            environment["PATH"] = pathEntry + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            ProcessResult result = this.RunWithLockHolder(
                "-c alias.gvfs-invalid-path-alias=version gvfs-invalid-path-alias",
                environment,
                shouldWait: false);
            result.ExitCode.ShouldEqual(0);
            result.Output.ShouldContain("git version");
        }

        private ProcessStartInfo CreateGitStartInfo(string command, Dictionary<string, string> environment = null)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(Properties.Settings.Default.PathToGit, command);
            startInfo.WorkingDirectory = this.Enlistment.RepoRoot;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }

            return startInfo;
        }

        private ProcessResult RunWithLockHolder(string command, Dictionary<string, string> environment = null, bool shouldWait = true)
        {
            using (Process holder = new Process())
            using (Process git = new Process())
            using (ManualResetEventSlim waiting = new ManualResetEventSlim(false))
            {
                holder.StartInfo = new ProcessStartInfo(GitHelpers.LockHolderCommandPath)
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                holder.Start().ShouldBeTrue();
                bool gitStarted = false;
                StringBuilder output = new StringBuilder();
                StringBuilder errors = new StringBuilder();
                Thread errorReader = null;
                try
                {
                    this.Enlistment.WaitForLock("GVFS.FunctionalTests.LockHolder (PID:" + holder.Id + ")")
                        .ShouldBeTrue("The expected lock-holder process did not acquire the lock.");
                    git.StartInfo = this.CreateGitStartInfo(command, environment);
                    git.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            output.AppendLine(args.Data);
                        }
                    };
                    errorReader = new Thread(() =>
                    {
                        int character;
                        while ((character = git.StandardError.Read()) != -1)
                        {
                            errors.Append((char)character);
                            if (!waiting.IsSet && errors.ToString().Contains(ExpectedStatusWaitingText))
                            {
                                waiting.Set();
                            }
                        }
                    });
                    gitStarted = git.Start();
                    gitStarted.ShouldBeTrue();
                    git.BeginOutputReadLine();
                    errorReader.Start();

                    if (shouldWait)
                    {
                        waiting.Wait(AcquireGVFSLockTimeout).ShouldBeTrue("Git did not wait for the lock holder.");
                        this.ReleaseLockHolder(holder);
                    }

                    git.WaitForExit(30000).ShouldBeTrue("Git did not finish within the test timeout.");
                    git.WaitForExit();
                    errorReader.Join(10000).ShouldBeTrue("Git stderr did not close.");
                    if (!shouldWait)
                    {
                        holder.HasExited.ShouldBeFalse();
                        this.Enlistment.Status().ShouldContain("GVFS.FunctionalTests.LockHolder (PID:" + holder.Id + ")");
                    }

                    return new ProcessResult(output.ToString(), errors.ToString(), git.ExitCode);
                }
                finally
                {
                    this.ReleaseLockHolder(holder);
                    if (gitStarted && !git.WaitForExit(10000))
                    {
                        git.Kill(entireProcessTree: true);
                        git.WaitForExit();
                    }

                    if (errorReader != null && (errorReader.ThreadState & System.Threading.ThreadState.Unstarted) == 0)
                    {
                        errorReader.Join(10000).ShouldBeTrue("Git stderr did not close during cleanup.");
                    }
                }
            }
        }

        private void ReleaseLockHolder(Process holder)
        {
            if (!holder.HasExited)
            {
                holder.StandardInput.WriteLine("done");
                holder.StandardInput.Close();
                if (!holder.WaitForExit(10000))
                {
                    holder.Kill(entireProcessTree: true);
                    holder.WaitForExit();
                    Assert.Fail("The lock-holder process did not exit.");
                }
            }

            holder.ExitCode.ShouldEqual(0, holder.StandardError.ReadToEnd());
        }
    }
}
