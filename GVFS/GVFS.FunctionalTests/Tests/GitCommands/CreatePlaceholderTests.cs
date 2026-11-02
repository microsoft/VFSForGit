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
        [TestCase("rev-list --stdin --quiet --all")]
        [TestCase("update-ref --stdin")]
        public void AllowsPlaceholderCreationWhileGitCommandIsRunning(string commandToRun)
        {
            this.CheckPlaceholderCreation(commandToRun, shouldAllow: true);
        }

        [TestCase("checkout-index --stdin")]
        [TestCase("fetch-pack --stdin URL")]
        [TestCase("notes copy --stdin")]
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
        public void AllowsPlaceholderCreationWhileWorktreeListIsRunning(string command)
        {
            string hookListPath = this.Enlistment.GetVirtualPathTo(".git", "hooks", "post-command.hooks");
            string originalHookList = File.ReadAllText(hookListPath);
            string eventName = "GVFS.WorktreeList." + Guid.NewGuid().ToString("N");
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
