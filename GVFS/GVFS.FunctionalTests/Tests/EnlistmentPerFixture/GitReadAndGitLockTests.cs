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

        [TestCase("status")]
        [TestCase("for-each-repo")]
        [Order(4)]
        public void GitCommandWaitsWhileAnotherIsRunning(string command)
        {
            string emptyConfigPath = null;
            Dictionary<string, string> environmentVariables = null;
            try
            {
                if (command == "for-each-repo")
                {
                    emptyConfigPath = Path.Combine(this.Enlistment.EnlistmentRoot, "empty-git-config-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(emptyConfigPath, string.Empty);
                    environmentVariables = new Dictionary<string, string>
                    {
                        { "GIT_CONFIG_GLOBAL", emptyConfigPath },
                        { "GIT_CONFIG_NOSYSTEM", "1" },
                        { "GIT_CONFIG_COUNT", "0" },
                        { "GIT_CONFIG_PARAMETERS", string.Empty },
                    };

                    ProcessResult alias = GitProcess.InvokeProcess(
                        this.Enlistment.RepoRoot,
                        "config --get alias.for-each-repo",
                        environmentVariables);
                    alias.ExitCode.ShouldEqual(1);
                    alias.Output.ShouldBeEmpty();

                    command = "for-each-repo --config=gvfs.test-lock-policy-repos-" + Guid.NewGuid().ToString("N");
                }

                int pid;
                GitHelpers.AcquireGVFSLock(this.Enlistment, out pid, resetTimeout: 3000);

                ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    command,
                    environmentVariables,
                    removeWaitingMessages: false);
                result.ExitCode.ShouldEqual(0);
                result.Errors.ShouldContain(ExpectedStatusWaitingText);
            }
            finally
            {
                if (emptyConfigPath != null)
                {
                    File.Delete(emptyConfigPath);
                }
            }
        }

        [TestCase("worktree list")]
        [TestCase("worktree list --porcelain")]
        [TestCase("worktree list --porcelain -z")]
        [TestCase("annotate HEAD -- Readme.md")]
        [TestCase("check-ref-format --branch topic")]
        [TestCase("cherry HEAD HEAD")]
        [TestCase("count-objects -vH")]
        [TestCase("ls-remote --get-url origin")]
        [TestCase("range-diff HEAD~1..HEAD HEAD~1..HEAD")]
        [TestCase("reflog")]
        [TestCase("reflog show --all")]
        [TestCase("reflog list")]
        [TestCase("reflog exists HEAD")]
        [TestCase("describe --always HEAD")]
        [TestCase("grep -n -e . HEAD -- Readme.md")]
        [TestCase("grep --cached -e . -- Readme.md")]
        [TestCase("refs list --count=1")]
        [TestCase("merge-file -p .git/HEAD .git/HEAD .git/HEAD")]
        [TestCase("merge-file --object-id HEAD:Readme.md HEAD:Readme.md HEAD:Readme.md")]
        [TestCase("merge-tree --write-tree HEAD HEAD")]
        [TestCase("notes list")]
        [TestCase("replace -l")]
        [TestCase("format-patch --stdout -1 HEAD")]
        [TestCase("var GIT_AUTHOR_IDENT")]
        [TestCase("shortlog -sn HEAD")]
        [TestCase("show-branch --list")]
        public void ReadOnlyCommandDoesNotWaitForLock(string command)
        {
            GitHelpers.RunWithGVFSLock(
                this.Enlistment,
                () =>
                {
                    ProcessResult result = GitProcess.InvokeProcess(
                        this.Enlistment.RepoRoot,
                        command,
                        timeoutMs: AcquireGVFSLockTimeout);

                    result.ExitCode.ShouldEqual(0);
                    result.Errors.ShouldNotContain(false, ExpectedStatusWaitingText);
                });
        }

        [TestCase("describe --dirty --always")]
        [TestCase("describe --broken --always")]
        [TestCase("describe --di --always")]
        [TestCase("grep -nOcat -e . -- Readme.md")]
        [TestCase("grep --open=cat -e . -- Readme.md")]
        [TestCase("refs migrate -h")]
        public void UnsafeCommandModeStillWaitsForLock(string command)
        {
            File.ReadAllText(this.Enlistment.GetVirtualPathTo("Readme.md"));
            ProcessResult result = this.RunWithLockHolder(command);
            result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MergeFileOnlyBypassesLockWhenOutputModeRemainsEnabled(bool disableStdout)
        {
            string inputPath = Path.Combine(this.Enlistment.EnlistmentRoot, "merge-file-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(inputPath, "merge input\n");
            try
            {
                string command = $"merge-file --stdout {(disableStdout ? "--no-stdout" : string.Empty)} \"{inputPath}\" \"{inputPath}\" \"{inputPath}\"";
                ProcessResult result = this.RunWithLockHolder(command, shouldWait: disableStdout);
                result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
                result.Errors.Contains(ExpectedStatusWaitingText).ShouldEqual(disableStdout);
                File.ReadAllText(inputPath).ShouldEqual("merge input\n");
            }
            finally
            {
                File.Delete(inputPath);
            }
        }

        [TestCase]
        public void ReflogWritesPreserveIndexAndTrackedContentUnderLockContention()
        {
            string refName = "refs/heads/gvfs-reflog-test-" + Guid.NewGuid().ToString("N");
            string filePath = this.Enlistment.GetVirtualPathTo("Readme.md");
            string indexPath = this.Enlistment.GetDotGitPath("index");
            string originalContent = File.ReadAllText(filePath);
            string oldObject = GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse HEAD~1").Trim();
            string newObject = GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse HEAD").Trim();
            try
            {
                File.WriteAllText(filePath, originalContent + "\nstaged reflog test\n");
                GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "add -- Readme.md");
                string dirtyContent = originalContent + "\nunstaged reflog test\n";
                File.WriteAllText(filePath, dirtyContent);
                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"update-ref --create-reflog -m first {refName} {oldObject}");
                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"update-ref -m second {refName} {newObject}");

                GitHelpers.RunWithGVFSLock(
                    this.Enlistment,
                    () =>
                    {
                        byte[] index = File.ReadAllBytes(indexPath);
                        DateTime indexWriteTime = File.GetLastWriteTimeUtc(indexPath);
                        (string Command, string ExpectedRef, int ReflogEntries)[] commands =
                        {
                            ($"reflog write {refName} {newObject} {newObject} appended", newObject, 3),
                            ($"reflog delete --rewrite --updateref {refName}@{{0}}", newObject, 2),
                            ($"reflog delete --rewrite --updateref {refName}@{{0}}", oldObject, 1),
                            ($"reflog expire --stale-fix --rewrite --updateref --expire=never --expire-unreachable=never {refName}", oldObject, 1),
                            ($"reflog expire --stale-fix --rewrite --updateref --expire=all {refName}", oldObject, 0),
                            ($"reflog drop {refName}", oldObject, -1),
                        };

                        foreach ((string command, string expectedRef, int reflogEntries) in commands)
                        {
                            ProcessResult result = GitProcess.InvokeProcess(
                                this.Enlistment.RepoRoot,
                                command,
                                timeoutMs: AcquireGVFSLockTimeout);
                            result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
                            result.Errors.ShouldNotContain(false, ExpectedStatusWaitingText);
                            GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse " + refName).Trim().ShouldEqual(expectedRef);
                            if (reflogEntries < 0)
                            {
                                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reflog exists " + refName).ExitCode.ShouldEqual(1);
                            }
                            else
                            {
                                ProcessResult reflog = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reflog show --format=%H " + refName);
                                reflog.ExitCode.ShouldEqual(0, reflog.Errors);
                                reflog.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length.ShouldEqual(reflogEntries);
                            }

                            File.ReadAllBytes(indexPath).ShouldMatchInOrder(index);
                            File.GetLastWriteTimeUtc(indexPath).ShouldEqual(indexWriteTime);
                            File.ReadAllText(filePath).ShouldEqual(dirtyContent);
                        }
                    });
            }
            finally
            {
                GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "reset --hard");
                GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "update-ref -d " + refName);
            }
        }

        [TestCase("")]
        [TestCase("with space")]
        [TestCase("with\ttab")]
        [TestCase("with\"quote")]
        [TestCase("trailing space slash\\")]
        [TestCase("before\\\\\"quote")]
        public void HookLoaderPreservesArgumentBoundaries(string argument)
        {
            string loaderPath = this.Enlistment.GetDotGitPath("hooks", "pre-command.exe");
            string hookListPath = this.Enlistment.GetDotGitPath("hooks", "pre-command.hooks");
            string originalHookList = File.ReadAllText(hookListPath);
            try
            {
                File.WriteAllText(hookListPath, $"\"{GitHelpers.LockHolderCommandPath}\" --echo-arguments");
                ProcessStartInfo loader = new ProcessStartInfo(loaderPath)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                loader.ArgumentList.Add("first");
                loader.ArgumentList.Add(argument);
                loader.ArgumentList.Add("last");
                loader.Environment.Remove("GITHOOKSLOADER_PERFTRACE");
                ProcessResult result = ProcessHelper.Run(loader, timeoutMs: AcquireGVFSLockTimeout);
                result.ExitCode.ShouldEqual(0, result.Errors);
                result.Output.Split('\0').ShouldMatchInOrder("pre-command", "first", argument, "last");
            }
            finally
            {
                File.WriteAllText(hookListPath, originalHookList);
            }
        }

        [TestCase("rm -f")]
        [TestCase("foo --")]
        public void GrepOptionValuesCannotHidePagerDispatch(string pattern)
        {
            File.ReadAllText(this.Enlistment.GetVirtualPathTo("Readme.md"));
            string[] arguments = { "grep", "-e", pattern, "-Ocat", "-e", ".", "--", "Readme.md" };
            ProcessResult result = this.RunWithLockHolder(string.Empty, argumentList: arguments);
            // Git grep returns 1 when no line matches.
            Assert.That(result.ExitCode, Is.InRange(0, 1), result.Output + result.Errors);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase("v1* --")]
        [TestCase("v\"")]
        public void DescribeOptionValuesCannotHideIndexRefresh(string pattern)
        {
            string[] arguments = { "describe", "--match", pattern, "--dirty", "--always" };
            ProcessResult result = this.RunWithLockHolder(string.Empty, argumentList: arguments);
            result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase("ours -p")]
        [TestCase("ours\t-p")]
        [TestCase("ours\"")]
        public void MergeFileLabelsCannotEnableAnOutputMode(string label)
        {
            string inputPath = Path.Combine(this.Enlistment.EnlistmentRoot, "merge-label-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(inputPath, "merge input\n");
            try
            {
                string[] arguments = { "merge-file", "-L", label, inputPath, inputPath, inputPath };
                ProcessResult result = this.RunWithLockHolder(string.Empty, argumentList: arguments);
                result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
                result.Errors.ShouldContain(ExpectedStatusWaitingText);
                File.ReadAllText(inputPath).ShouldEqual("merge input\n");
            }
            finally
            {
                File.Delete(inputPath);
            }
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
            File.WriteAllText(scriptPath, "#!/bin/sh\necho external-script\nexec git status --porcelain\n");
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
                result.Output.ShouldContain("external-script");
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

        [TestCase]
        [Order(15)]
        public void DirectoryNamedAfterAnExternalDoesNotHideAnAlias()
        {
            const string CommandName = "gvfs-directory-alias";
            string directory = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.DirectoryNamedAfterAnExternalDoesNotHideAnAlias));
            string commandDirectory = Path.Combine(directory, "git-" + CommandName);
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

            // The test calls both hooks directly, so it does not depend on Git running post-command
            // for git-<name>.
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
                    hook.Arguments = "post-command" + commandArguments + " --exit_code=1";
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

        [TestCase]
        [Order(18)]
        public void SubsectionAliasInnerCommandStillAcquiresLock()
        {
            ProcessResult result = this.RunWithLockHolder(
                "-c \"alias.gvfs-subsection-alias.command=status --porcelain\" gvfs-subsection-alias");
            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase]
        [Order(19)]
        public void ConfigFileAliasInnerCommandStillAcquiresLock()
        {
            const string AliasName = "gvfs-config-file-alias";
            GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local alias." + AliasName + " \"status --porcelain\"");
            try
            {
                ProcessResult result = this.RunWithLockHolder(AliasName);
                result.ExitCode.ShouldEqual(0);
                result.Errors.ShouldContain(ExpectedStatusWaitingText);
            }
            finally
            {
                GitHelpers.CheckGitCommandAgainstGVFSRepo(this.Enlistment.RepoRoot, "config --local --unset alias." + AliasName);
            }
        }

        [TestCase]
        [Order(20)]
        public void SubsectionAliasWithCapitalLettersSkipsOnlyTheOuterLock()
        {
            ProcessResult result = this.RunWithLockHolder(
                "-c \"alias.GVFS-Subsection-Alias.command=status --porcelain\" GVFS-Subsection-Alias");
            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase]
        [Order(21)]
        public void UnreadableAliasConfigStopsTheCommandBeforeTheLock()
        {
            string hooksExecutable = Path.Combine(Path.GetDirectoryName(Properties.Settings.Default.PathToGVFS), "GVFS.Hooks.exe");
            string configPath = Path.Combine(this.Enlistment.EnlistmentRoot, nameof(this.UnreadableAliasConfigStopsTheCommandBeforeTheLock) + ".gitconfig");
            File.WriteAllText(configPath, "[alias]\n\tgvfs-unreadable = status\n");

            // The hook reads aliases with the parent Git's executable, so keep a Git process alive.
            using (Process git = new Process())
            {
                git.StartInfo = this.CreateGitStartInfo("hash-object --stdin");
                git.StartInfo.RedirectStandardInput = true;
                bool started = false;
                try
                {
                    started = git.Start();
                    started.ShouldBeTrue();
                    Dictionary<string, string> environment = new Dictionary<string, string>();
                    environment["GIT_CONFIG_GLOBAL"] = configPath;
                    ProcessStartInfo hook = this.CreateGitStartInfo("pre-command git-gvfs-unreadable --git-pid=" + git.Id, environment);
                    hook.FileName = hooksExecutable;

                    ProcessResult preCommand;
                    using (new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        preCommand = ProcessHelper.Run(hook, timeoutMs: 30000);
                    }

                    preCommand.ExitCode.ShouldEqual(1, preCommand.Output + preCommand.Errors);
                    preCommand.Output.ShouldContain(
                        "Unable to read Git aliases (exit code 128).",
                        "GVFS cannot tell if 'git gvfs-unreadable' runs an alias or a program, so it stopped the command.");
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
                        }
                    }

                    File.Delete(configPath);
                }
            }
        }

        [TestCase("alias..gvfs-dot-prefix.foo", ".gvfs-dot-prefix.foo")]
        [TestCase("alias..gvfs-dot-subsection.command", ".gvfs-dot-subsection")]
        [Order(22)]
        public void DotPrefixedSubsectionAliasInnerCommandStillAcquiresLock(string aliasKey, string aliasName)
        {
            ProcessResult result = this.RunWithLockHolder(
                "-c \"" + aliasKey + "=status --porcelain\" " + aliasName);
            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldContain(ExpectedStatusWaitingText);
        }

        [TestCase]
        [Order(23)]
        public void PostCommandForExternalIgnoresAnUnmountedEnlistment()
        {
            string hooksExecutable = Path.Combine(Path.GetDirectoryName(Properties.Settings.Default.PathToGVFS), "GVFS.Hooks.exe");
            string directory = Path.Combine(Path.GetTempPath(), nameof(this.PostCommandForExternalIgnoresAnUnmountedEnlistment) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, ".gvfs"));
            try
            {
                ProcessStartInfo hook = this.CreateGitStartInfo("post-command git-gvfs-unmounted --exit_code=0");
                hook.FileName = hooksExecutable;
                hook.WorkingDirectory = directory;

                // A false GIT_OPTIONAL_LOCKS makes post-command status skip the lock request.
                hook.Environment.Remove("GIT_OPTIONAL_LOCKS");
                ProcessResult external = ProcessHelper.Run(hook, timeoutMs: 30000);
                external.ExitCode.ShouldEqual(0, external.Output + external.Errors);
                (external.Output + external.Errors).ShouldEqual(string.Empty);

                hook.Arguments = "post-command status --exit_code=0";
                ProcessResult builtIn = ProcessHelper.Run(hook, timeoutMs: 30000);
                builtIn.ExitCode.ShouldEqual(1, builtIn.Output + builtIn.Errors);
                builtIn.Output.ShouldContain("The repo does not appear to be mounted.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
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

        private ProcessResult RunWithLockHolder(
            string command,
            Dictionary<string, string> environment = null,
            bool shouldWait = true,
            string[] argumentList = null)
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
                bool completed = false;
                bool released = false;
                StringBuilder output = new StringBuilder();
                StringBuilder errors = new StringBuilder();
                Thread errorReader = null;
                try
                {
                    this.Enlistment.WaitForLock("GVFS.FunctionalTests.LockHolder (PID:" + holder.Id + ")")
                        .ShouldBeTrue("The expected lock-holder process did not acquire the lock.");
                    git.StartInfo = this.CreateGitStartInfo(command, environment);
                    if (argumentList != null)
                    {
                        foreach (string argument in argumentList)
                        {
                            git.StartInfo.ArgumentList.Add(argument);
                        }
                    }

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
                        released = true;
                    }

                    git.WaitForExit(30000).ShouldBeTrue("Git did not finish within the test timeout.");
                    git.WaitForExit();
                    errorReader.Join(10000).ShouldBeTrue("Git stderr did not close.");
                    if (!shouldWait)
                    {
                        holder.HasExited.ShouldBeFalse();
                        this.Enlistment.Status().ShouldContain("GVFS.FunctionalTests.LockHolder (PID:" + holder.Id + ")");
                    }

                    completed = true;
                    return new ProcessResult(output.ToString(), errors.ToString(), git.ExitCode);
                }
                finally
                {
                    // Clean up Git and its reader even if the holder release fails. After a test
                    // failure, report that failure, not a cleanup failure.
                    try
                    {
                        if (!released)
                        {
                            this.ReleaseLockHolder(holder, verify: completed);
                        }
                    }
                    finally
                    {
                        if (gitStarted && !git.WaitForExit(10000))
                        {
                            git.Kill(entireProcessTree: true);
                            git.WaitForExit();
                        }

                        if (errorReader != null && (errorReader.ThreadState & System.Threading.ThreadState.Unstarted) == 0)
                        {
                            bool readerClosed = errorReader.Join(10000);
                            if (completed)
                            {
                                readerClosed.ShouldBeTrue("Git stderr did not close during cleanup.");
                            }
                        }
                    }
                }
            }
        }

        private void ReleaseLockHolder(Process holder, bool verify = true)
        {
            if (!holder.HasExited)
            {
                try
                {
                    holder.StandardInput.WriteLine("done");
                    holder.StandardInput.Close();
                }
                catch (IOException) when (!verify || holder.HasExited)
                {
                    // The holder already exited, or cleanup after a failed test is best effort.
                }

                if (!holder.WaitForExit(10000))
                {
                    holder.Kill(entireProcessTree: true);
                    holder.WaitForExit();
                    if (verify)
                    {
                        Assert.Fail("The lock-holder process did not exit.");
                    }
                }
            }

            if (verify)
            {
                holder.ExitCode.ShouldEqual(0, holder.StandardError.ReadToEnd());
            }
        }
    }
}
