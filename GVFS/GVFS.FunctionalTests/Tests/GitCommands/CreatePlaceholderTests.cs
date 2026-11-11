using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class CreatePlaceholderTests : GitRepoTests
    {
        private static readonly string FileToRead = Path.Combine("GVFS", "GVFS", "Program.cs");

        public CreatePlaceholderTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void BoundedPlaceholderWaitResumesOrReturnsTransientFailure(bool releaseBeforeTimeout, bool materializeWhileWaiting)
        {
            this.EnablePlaceholderLockWait();
            string expected = File.ReadAllText(Path.Combine(this.ControlGitRepo.RootPath, FileToRead));
            using (Process git = this.CreateWaitingCheckout())
            {
                git.Start().ShouldBeTrue();
                try
                {
                    this.Enlistment.WaitForLock("git checkout-index --stdin").ShouldBeTrue();
                    Task<string> read = Task.Factory.StartNew(
                        () => File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToRead)),
                        TaskCreationOptions.LongRunning);
                    SpinWait.SpinUntil(
                        () => Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_mount_*.log")
                            .Any(path => GVFSHelpers.ReadAllTextFromWriteLockedFile(path)
                                .Split('\n').Any(line => line.Contains("PlaceholderLockWait") &&
                                    line.Contains("\"outcome\":\"Queued\"") && line.Contains("Program.cs"))),
                        TimeSpan.FromSeconds(10)).ShouldBeTrue("The read did not reach the deferred callback.");

                    if (releaseBeforeTimeout)
                    {
                        read.IsCompleted.ShouldBeFalse("The first open did not wait for Git.");
                        if (materializeWhileWaiting)
                        {
                            git.StandardInput.Write(GitHelpers.ConvertPathToGitFormat(FileToRead) + "\n");
                        }

                        git.StandardInput.Close();
                        Task.WhenAny(read, Task.Delay(10000)).GetAwaiter().GetResult().ShouldEqual(read);
                        read.GetAwaiter().GetResult().ShouldEqual(expected);
                    }
                    else
                    {
                        Task.WhenAny(read, Task.Delay(10000)).GetAwaiter().GetResult().ShouldEqual(read);
                        IOException error = Assert.Throws<IOException>(() => read.GetAwaiter().GetResult());
                        error.HResult.ShouldEqual(unchecked((int)0x80070020));
                        git.HasExited.ShouldBeFalse("The timeout must not require Git to exit.");
                        IOException retryError = Assert.Throws<IOException>(
                            () => File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToRead)));
                        retryError.HResult.ShouldEqual(unchecked((int)0x80070020));
                        git.StandardInput.Close();
                    }

                    git.WaitForExit(10000).ShouldBeTrue();
                    git.ExitCode.ShouldEqual(0, git.StandardError.ReadToEnd());
                    File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToRead)).ShouldEqual(expected);
                }
                finally
                {
                    this.StopWaitingCheckout(git);
                }
            }
        }

        [TestCase]
        public void PlaceholderWaitPreservesGitCheckoutWrites()
        {
            this.EnablePlaceholderLockWait();
            using (Process git = this.CreateWaitingCheckout())
            {
                git.Start().ShouldBeTrue();
                try
                {
                    this.Enlistment.WaitForLock("git checkout-index --stdin").ShouldBeTrue();
                    git.StandardInput.Write(GitHelpers.ConvertPathToGitFormat(FileToRead) + "\n");
                    git.StandardInput.Close();
                    git.WaitForExit(15000).ShouldBeTrue();
                    git.ExitCode.ShouldEqual(0, git.StandardError.ReadToEnd());
                    this.FileContentsShouldMatch(FileToRead);
                }
                finally
                {
                    this.StopWaitingCheckout(git);
                }
            }
        }

        private void EnablePlaceholderLockWait()
        {
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "config gvfs.placeholder-lock-wait true").ExitCode.ShouldEqual(0);
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();
        }

        private Process CreateWaitingCheckout()
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo(Settings.Default.PathToGit, "checkout-index --stdin")
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
        }

        private void StopWaitingCheckout(Process git)
        {
            if (!git.HasExited)
            {
                git.StandardInput.Close();
                if (!git.WaitForExit(10000))
                {
                    git.Kill(entireProcessTree: true);
                    git.WaitForExit(10000).ShouldBeTrue();
                }
            }
        }

        [TestCase]
        public void AllowsPlaceholderCreationForDefaultAnnotate()
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                $"annotate -- \"{GitHelpers.ConvertPathToGitFormat(FileToRead)}\"");

            result.ExitCode.ShouldEqual(0);
            result.Errors.ShouldBeEmpty();
            string.IsNullOrWhiteSpace(result.Output).ShouldBeFalse("Annotate did not return file annotations.");
        }

        [TestCase("check-attr --stdin --all")]
        [TestCase("check-ignore --stdin")]
        [TestCase("check-mailmap --stdin")]
        [TestCase("diff-tree --stdin")]
        [TestCase("hash-object --stdin")]
        [TestCase("index-pack --stdin")]
        [TestCase("name-rev --stdin")]
        [TestCase("notes copy --stdin")]
        [TestCase("rev-list --stdin --quiet --all")]
        [TestCase("update-ref --stdin")]
        public void AllowsPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: true);
        }

        [TestCase("checkout-index --stdin")]
        [TestCase("fetch-pack --stdin URL")]
        [TestCase("reset --stdin")]
        [TestCase("send-pack --stdin URL")]
        [TestCase("update-index --stdin")]
        public void BlocksPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: false);
        }

        [TestCase("worktree list")]
        [TestCase("worktree list --porcelain")]
        [TestCase("worktree list --porcelain -z")]
        [TestCase("reflog")]
        [TestCase("describe --always HEAD")]
        [TestCase("grep -q -e . HEAD -- Readme.md")]
        [TestCase("refs list --count=1")]
        [TestCase("merge-file --object-id HEAD:Readme.md HEAD:Readme.md HEAD:Readme.md")]
        public void AllowsPlaceholderCreationWhileExemptCommandIsRunning(string command)
        {
            string hookListPath = this.Enlistment.GetVirtualPathTo(".git", "hooks", "post-command.hooks");
            string originalHookList = File.ReadAllText(hookListPath);
            string eventName = "GVFS.ExemptCommand." + Guid.NewGuid().ToString("N");
            string expectedContent = File.ReadAllText(Path.Combine(this.ControlGitRepo.RootPath, FileToRead));

            using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Ready"))
            using (EventWaitHandle release = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Release"))
            using (Process git = new Process())
            {
                git.StartInfo = new ProcessStartInfo(Settings.Default.PathToGit, command)
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                git.StartInfo.Environment["GVFS_TEST_HOOK_WAIT_EVENT_NAME"] = eventName;

                bool started = false;
                try
                {
                    // Pause the post-command chain before GVFS can release an incorrectly acquired lock.
                    string waitHook = $"\"{GitHelpers.LockHolderCommandPath}\" --wait-for-event";
                    File.WriteAllText(hookListPath, waitHook + Environment.NewLine + originalHookList);

                    started = git.Start();
                    started.ShouldBeTrue();
                    bool reachedHook = ready.WaitOne(TimeSpan.FromSeconds(30));
                    if (!reachedHook && git.HasExited)
                    {
                        Assert.Fail($"Git exited before the wait hook. Exit code: {git.ExitCode}. Errors: {git.StandardError.ReadToEnd()}");
                    }

                    reachedHook.ShouldBeTrue("Git did not reach the post-command hook.");

                    File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToRead)).ShouldEqual(expectedContent);

                    release.Set();
                    git.WaitForExit(10000).ShouldBeTrue("Git did not exit after the test released the hook.");
                    git.ExitCode.ShouldEqual(0);
                    git.StandardError.ReadToEnd().ShouldBeEmpty();
                }
                finally
                {
                    release.Set();
                    if (started && !git.WaitForExit(10000))
                    {
                        git.Kill(entireProcessTree: true);
                        git.WaitForExit();
                    }

                    File.WriteAllText(hookListPath, originalHookList);
                }
            }
        }

        private void CheckPlaceholderCreation(string command, bool shouldAllow)
        {
            string eofCharacter = "\x04";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                eofCharacter = "\x1A";
            }

            this.EditFile($"Some new content for {command}.", "Protocol.md");
            ManualResetEventSlim resetEvent = GitHelpers.RunGitCommandWithWaitAndStdIn(this.Enlistment, resetTimeout: 3000, command: $"{command}", stdinToQuit: eofCharacter, processId: out _);

            if (shouldAllow)
            {
                this.FileContentsShouldMatch(FileToRead);
            }
            else
            {
                string virtualPath = Path.Combine(this.Enlistment.RepoRoot, FileToRead);
                string controlPath = Path.Combine(this.ControlGitRepo.RootPath, FileToRead);
                virtualPath.ShouldNotExistOnDisk(this.FileSystem);
                controlPath.ShouldBeAFile(this.FileSystem);
            }

            this.ValidateGitCommand("--no-optional-locks status");
            resetEvent.Wait();
            this.RunGitCommand("reset --hard");
        }
    }
}
