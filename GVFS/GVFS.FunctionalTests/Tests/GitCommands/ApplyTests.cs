using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class ApplyTests : GitRepoTests
    {
        private const uint FileReadAttributes = 0x80;
        private static readonly string FileToPatch = Path.Combine("GVFS", "GVFS", "Program.cs");

        public ApplyTests()
            : base(enlistmentPerTest: true, validateWorkingTree: Settings.ValidateWorkingTreeMode.None)
        {
        }

        public enum ProjectedFileState
        {
            Virtual,
            Placeholder,
            Hydrated,
        }

        [TestCase(ProjectedFileState.Virtual, false, false, "")]
        [TestCase(ProjectedFileState.Placeholder, false, false, "")]
        [TestCase(ProjectedFileState.Hydrated, false, false, "")]
        [TestCase(ProjectedFileState.Virtual, true, false, "")]
        [TestCase(ProjectedFileState.Virtual, false, true, "")]
        [TestCase(ProjectedFileState.Virtual, false, false, "--check")]
        public void ApplyToProjectedFile(ProjectedFileState state, bool waitForPlaceholders, bool stdin, string options)
        {
            this.AssertFilesAreVirtual(waitForPlaceholders ? 2000 : 0, FileToPatch);
            string virtualPath = this.Enlistment.GetVirtualPathTo(FileToPatch);

            if (state == ProjectedFileState.Placeholder)
            {
                using (SafeFileHandle handle = NativeMethods.CreateFile(
                    virtualPath,
                    FileReadAttributes,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    0,
                    IntPtr.Zero))
                {
                    handle.IsInvalid.ShouldBeFalse("Could not open file metadata. Error: " + Marshal.GetLastWin32Error());
                }

                ((int)File.GetAttributes(virtualPath) & FileSystemShouldExtensions.FileAttributeRecallOnDataAccess)
                    .ShouldNotEqual(0, "The file must have no hydrated contents before apply.");
            }
            else if (state == ProjectedFileState.Hydrated)
            {
                this.FileContentsShouldMatch(FileToPatch);
            }

            string gitPath = GitHelpers.ConvertPathToGitFormat(FileToPatch);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.FileSystem, gitPath);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"ls-files -v -- {gitPath}")
                .Output.Trim().ShouldEqual("S " + gitPath);

            string controlPath = Path.Combine(this.ControlGitRepo.RootPath, FileToPatch);
            File.AppendAllText(controlPath, "\n// Apply functional test\n");
            string patch = this.CreatePatch("apply.patch");
            this.ApplyPatch(options, patch, stdin);
            this.FileContentsShouldMatch(FileToPatch);
            this.ValidateGitCommand("diff --cached --binary");
            this.ValidateGitCommand("status --porcelain");

            if (options == "--check")
            {
                this.ValidateGitCommand("diff --exit-code");
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ApplyOwnerCanOpenAfterAnotherRequesterIsRejected(bool waitForPlaceholders, bool probeOtherRequester)
        {
            this.AssertFilesAreVirtual(waitForPlaceholders ? 2000 : 0, FileToPatch);
            File.AppendAllText(Path.Combine(this.ControlGitRepo.RootPath, FileToPatch), "\n// Owner-scoped apply\n");
            string patch = this.CreatePatch("owner.patch");
            using (Process git = new Process())
            {
                git.StartInfo = new ProcessStartInfo(Settings.Default.PathToGit, "apply -")
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                git.Start().ShouldBeTrue();
                Task<string> output = git.StandardOutput.ReadToEndAsync();
                Task<string> errors = git.StandardError.ReadToEndAsync();
                try
                {
                    this.Enlistment.WaitForLock("git apply -").ShouldBeTrue();
                    if (probeOtherRequester)
                    {
                        IOException rejected = Assert.Throws<IOException>(
                            () => File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToPatch)));
                        rejected.HResult.ShouldEqual(unchecked((int)0x80070020));
                        using (SafeFileHandle handle = NativeMethods.CreateFile(
                            this.Enlistment.GetVirtualPathTo(FileToPatch),
                            FileReadAttributes,
                            FileShare.ReadWrite | FileShare.Delete,
                            IntPtr.Zero,
                            FileMode.Open,
                            0,
                            IntPtr.Zero))
                        {
                            int error = Marshal.GetLastWin32Error();
                            handle.IsInvalid.ShouldBeTrue("An unrelated reader must not create the placeholder.");
                            error.ShouldEqual(32, "Apply rejection must not cache a false absence.");
                        }
                    }

                    using (FileStream input = File.OpenRead(Path.Combine(this.Enlistment.RepoRoot, patch)))
                    {
                        input.CopyTo(git.StandardInput.BaseStream);
                    }

                    git.StandardInput.Close();
                    git.WaitForExit(15000).ShouldBeTrue("Apply did not finish after it received the patch.");
                    git.ExitCode.ShouldEqual(0, errors.GetAwaiter().GetResult());
                    ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, $"apply {patch}");
                    expected.ExitCode.ShouldEqual(0, expected.Errors);
                    output.GetAwaiter().GetResult().ShouldEqual(expected.Output);
                    this.FileContentsShouldMatch(FileToPatch);
                    this.ValidateGitCommand("status --porcelain");
                }
                finally
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
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplyAllowsPlaceholderCreationForAChildHook(bool waitForPlaceholders)
        {
            string childFile = Path.Combine("GVFS", "GVFS.Common", "GVFSConstants.cs");
            this.AssertFilesAreVirtual(waitForPlaceholders ? 2000 : 0, FileToPatch, childFile);
            File.AppendAllText(Path.Combine(this.ControlGitRepo.RootPath, FileToPatch), "\n// Apply with child hook\n");
            string patch = this.CreatePatch("child-hook.patch");
            string hooks = this.Enlistment.GetVirtualPathTo(".git", "hooks", "post-command.hooks");
            string original = File.ReadAllText(hooks);
            string eventName = "GVFS.ApplyChild." + Guid.NewGuid().ToString("N");
            using (EventWaitHandle opened = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            using (Process git = new Process())
            {
                git.StartInfo = new ProcessStartInfo(Settings.Default.PathToGit, "apply " + patch)
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                git.StartInfo.Environment["GVFS_TEST_PLACEHOLDER_PATH"] = this.Enlistment.GetVirtualPathTo(childFile);
                git.StartInfo.Environment["GVFS_TEST_PLACEHOLDER_EVENT_NAME"] = eventName;
                bool started = false;
                try
                {
                    File.WriteAllText(hooks, $"\"{GitHelpers.LockHolderCommandPath}\" --read-placeholder-success" + Environment.NewLine + original);
                    started = git.Start();
                    started.ShouldBeTrue();
                    Task<string> errors = git.StandardError.ReadToEndAsync();
                    git.WaitForExit(15000).ShouldBeTrue("The apply child hook did not finish.");
                    git.ExitCode.ShouldEqual(0, errors.GetAwaiter().GetResult());
                    opened.WaitOne(TimeSpan.FromSeconds(1)).ShouldBeTrue("The child did not open the virtual file under the apply lock.");
                    ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "apply " + patch);
                    expected.ExitCode.ShouldEqual(0, expected.Errors);
                    git.StandardOutput.ReadToEnd().ShouldEqual(expected.Output);
                }
                finally
                {
                    if (started && !git.HasExited)
                    {
                        git.Kill(entireProcessTree: true);
                        git.WaitForExit(10000).ShouldBeTrue();
                    }

                    File.WriteAllText(hooks, original);
                }
            }

            this.FileContentsShouldMatch(FileToPatch);
            this.FileContentsShouldMatch(childFile);
            this.ValidateGitCommand("status --porcelain");
        }

        [TestCase]
        public void ApplyOwnerCanReadWhileAnotherRequesterWaits()
        {
            this.AssertFilesAreVirtual(30000, FileToPatch);
            string controlPath = Path.Combine(this.ControlGitRepo.RootPath, FileToPatch);
            string originalContents = File.ReadAllText(controlPath);
            File.AppendAllText(controlPath, "\n// Overlapping apply reader\n");
            string patch = this.CreatePatch("overlapping-reader.patch");
            using (Process git = new Process())
            {
                git.StartInfo = new ProcessStartInfo(Settings.Default.PathToGit, "apply -")
                {
                    WorkingDirectory = this.Enlistment.RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                git.Start().ShouldBeTrue();
                Task<string> output = git.StandardOutput.ReadToEndAsync();
                Task<string> errors = git.StandardError.ReadToEndAsync();
                Task<string> otherRead = null;
                try
                {
                    this.Enlistment.WaitForLock("git apply -").ShouldBeTrue();
                    otherRead = Task.Factory.StartNew(
                        () => File.ReadAllText(this.Enlistment.GetVirtualPathTo(FileToPatch)),
                        TaskCreationOptions.LongRunning);
                    SpinWait.SpinUntil(
                        () => Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_mount_*.log")
                            .Any(path => GVFSHelpers.ReadAllTextFromWriteLockedFile(path)
                                .Split('\n').Any(line => line.Contains("PlaceholderLockWait") &&
                                    line.Contains("\"outcome\":\"Queued\"") &&
                                    line.Contains(FileToPatch.Replace("\\", "\\\\")))),
                        TimeSpan.FromSeconds(10)).ShouldBeTrue("The unrelated reader did not enter the wait queue.");
                    otherRead.IsCompleted.ShouldBeFalse("The unrelated reader must still wait before Git receives the patch.");

                    using (FileStream input = File.OpenRead(Path.Combine(this.Enlistment.RepoRoot, patch)))
                    {
                        input.CopyTo(git.StandardInput.BaseStream);
                    }

                    git.StandardInput.Close();
                    git.WaitForExit(10000).ShouldBeTrue("The owner did not finish before the unrelated reader's wait budget.");
                    git.ExitCode.ShouldEqual(0, errors.GetAwaiter().GetResult());
                    Task.WhenAny(otherRead, Task.Delay(10000)).GetAwaiter().GetResult().ShouldEqual(otherRead);
                    string concurrentContents = otherRead.GetAwaiter().GetResult();
                    ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, $"apply {patch}");
                    expected.ExitCode.ShouldEqual(0, expected.Errors);
                    output.GetAwaiter().GetResult().ShouldEqual(expected.Output);
                    bool readPreimage = concurrentContents == originalContents;
                    (readPreimage || concurrentContents == File.ReadAllText(controlPath))
                        .ShouldBeTrue("The concurrent reader must read a complete preimage or postimage.");
                    TestContext.WriteLine("ConcurrentReadImage=" + (readPreimage ? "preimage" : "postimage"));
                    this.FileContentsShouldMatch(FileToPatch);
                    this.ValidateGitCommand("status --porcelain");
                }
                finally
                {
                    if (!git.HasExited)
                    {
                        git.StandardInput.Close();
                        git.Kill(entireProcessTree: true);
                        git.WaitForExit(10000).ShouldBeTrue();
                        ProcessResult cleanup = GitProcess.InvokeProcess(
                            this.Enlistment.RepoRoot, "status --porcelain", timeoutMs: 10000);
                        cleanup.ExitCode.ShouldEqual(0, cleanup.Errors);
                    }

                    if (otherRead != null)
                    {
                        Task.WhenAny(otherRead, Task.Delay(10000)).GetAwaiter().GetResult().ShouldEqual(otherRead);
                        otherRead.GetAwaiter().GetResult();
                    }
                }
            }
        }

        [TestCase]
        public void ApplyAndReverseMixedPatch()
        {
            string deleted = Path.Combine("GVFS", "GVFS", "GVFS.csproj");
            string renamed = Path.Combine("GVFS", "GVFS.Common", "GVFSConstants.cs");
            string destination = Path.Combine("GVFS", "GVFS.Common", "renamed file \u00e9.cs");
            string added = Path.Combine("GVFS", "GVFS", "apply-added.txt");
            this.AssertFilesAreVirtual(0, FileToPatch, deleted, renamed);

            File.AppendAllText(Path.Combine(this.ControlGitRepo.RootPath, FileToPatch), "\n// Mixed patch\n");
            File.Delete(Path.Combine(this.ControlGitRepo.RootPath, deleted));
            File.Move(Path.Combine(this.ControlGitRepo.RootPath, renamed), Path.Combine(this.ControlGitRepo.RootPath, destination));
            File.WriteAllText(Path.Combine(this.ControlGitRepo.RootPath, added), "Added by apply\n");
            string patch = this.CreatePatch("mixed.patch");

            this.ApplyPatch(string.Empty, patch);
            this.FileContentsShouldMatch(FileToPatch);
            this.FileContentsShouldMatch(destination);
            this.FileContentsShouldMatch(added);
            this.Enlistment.GetVirtualPathTo(deleted).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.GetVirtualPathTo(renamed).ShouldNotExistOnDisk(this.FileSystem);
            this.ValidateGitCommand("diff --cached --binary");
            this.ValidateGitCommand("status --porcelain");

            this.ApplyPatch("--reverse", patch);
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();
            this.FileContentsShouldMatch(FileToPatch);
            this.FileContentsShouldMatch(deleted);
            this.FileContentsShouldMatch(renamed);
            this.Enlistment.GetVirtualPathTo(destination).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.GetVirtualPathTo(added).ShouldNotExistOnDisk(this.FileSystem);
            this.ValidateGitCommand("status --porcelain");
        }

        [TestCase]
        public void ApplyDoesNotOverwriteAnExistingVirtualFileWithANewFilePatch()
        {
            this.AssertFilesAreVirtual(0, FileToPatch);
            string gitPath = GitHelpers.ConvertPathToGitFormat(FileToPatch);
            string patch = $"diff --git a/{gitPath} b/{gitPath}\nnew file mode 100644\n--- /dev/null\n+++ b/{gitPath}\n@@ -0,0 +1 @@\n+Do not overwrite the tracked file.\n";
            string patchPath = ".git/new-file.patch";
            File.WriteAllText(Path.Combine(this.ControlGitRepo.RootPath, patchPath), patch);
            File.WriteAllText(Path.Combine(this.Enlistment.RepoRoot, patchPath), patch);
            ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "apply " + patchPath);
            expected.ExitCode.ShouldEqual(1, expected.Errors);
            ProcessResult actual = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "apply " + patchPath);
            actual.ExitCode.ShouldEqual(expected.ExitCode, actual.Errors);
            actual.Errors.ShouldEqual(expected.Errors);
            this.FileContentsShouldMatch(FileToPatch);
            this.ValidateGitCommand("status --porcelain");
        }

        [TestCase]
        public void ApplyDoesNotRecreateFileDeletedByEarlierPatch()
        {
            this.AssertFilesAreVirtual(0, FileToPatch);
            string controlPath = Path.Combine(this.ControlGitRepo.RootPath, FileToPatch);
            File.AppendAllText(controlPath, "\n// Modification after deletion\n");
            string modification = this.CreatePatch("modification.patch");
            File.Delete(controlPath);
            string deletion = this.CreatePatch("deletion.patch");

            ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, $"apply {deletion} {modification}");
            expected.ExitCode.ShouldEqual(1, expected.Errors);
            ProcessResult actual = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"apply {deletion} {modification}");
            actual.ExitCode.ShouldEqual(expected.ExitCode, actual.Errors);
            actual.Errors.ShouldEqual(expected.Errors);
            this.Enlistment.GetVirtualPathTo(FileToPatch).ShouldNotExistOnDisk(this.FileSystem);
            this.ValidateGitCommand("status --porcelain");
        }

        private void AssertFilesAreVirtual(int placeholderWaitMs, params string[] paths)
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config gvfs.placeholder-lock-wait-ms " + placeholderWaitMs).ExitCode.ShouldEqual(0);

            this.Enlistment.UnmountGVFS();
            foreach (string path in paths)
            {
                this.Enlistment.GetVirtualPathTo(path).ShouldNotExistOnDisk(this.FileSystem);
            }

            this.Enlistment.MountGVFS();
        }

        private string CreatePatch(string name)
        {
            ProcessResult add = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "add -A");
            add.ExitCode.ShouldEqual(0, add.Errors);
            ProcessResult diff = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "diff --cached --binary --find-renames");
            diff.ExitCode.ShouldEqual(0, diff.Errors);
            string.IsNullOrWhiteSpace(diff.Output).ShouldBeFalse("The control repo must produce a patch.");
            ProcessResult reset = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, "reset --hard HEAD");
            reset.ExitCode.ShouldEqual(0, reset.Errors);
            File.WriteAllText(Path.Combine(this.ControlGitRepo.RootPath, ".git", name), diff.Output);
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(".git", name), diff.Output);
            return ".git/" + name;
        }

        private void ApplyPatch(string options, string patch, bool stdin = false)
        {
            string command = $"apply {options} {(stdin ? "-" : patch)}";
            using (FileStream expectedInput = stdin ? File.OpenRead(Path.Combine(this.ControlGitRepo.RootPath, patch)) : null)
            using (FileStream actualInput = stdin ? File.OpenRead(Path.Combine(this.Enlistment.RepoRoot, patch)) : null)
            {
                ProcessResult expected = GitProcess.InvokeProcess(this.ControlGitRepo.RootPath, command, inputStream: expectedInput);
                expected.ExitCode.ShouldEqual(0, expected.Errors);
                ProcessResult actual = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, inputStream: actualInput);
                actual.ExitCode.ShouldEqual(0, actual.Errors);
                actual.Output.ShouldEqual(expected.Output);
            }
        }
    }
}
