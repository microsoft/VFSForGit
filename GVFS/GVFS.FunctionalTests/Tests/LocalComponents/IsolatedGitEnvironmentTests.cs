using GVFS.Common.Git;
using GVFS.FunctionalTests.Properties;
using GVFS.UnitTests.Category;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NativeMethods = GVFS.FunctionalTests.Tools.NativeMethods;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    public class IsolatedGitEnvironmentTests
    {
        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DisposeWaitsForTransientDirectorySharingViolations()
        {
            int retries = 0;
            SafeFileHandle directoryLock = null;
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment(
                retryDelay: milliseconds =>
                {
                    ++retries;
                    directoryLock?.Dispose();
                    Thread.Sleep(milliseconds);
                }))
            {
                string lockedDirectory = Path.Combine(environment.RootDirectory, "locked");
                Directory.CreateDirectory(lockedDirectory);
                File.WriteAllText(Path.Combine(lockedDirectory, "child.txt"), "cleanup");
                using (directoryLock = OpenDirectoryWithoutDeleteSharing(lockedDirectory))
                {
                    environment.Dispose();
                }

                Assert.That(retries, Is.GreaterThan(0), "Cleanup must encounter the held directory handle before the test releases it.");
                Assert.That(Directory.Exists(environment.RootDirectory), Is.False);
            }
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DisposePreservesPersistentDirectorySharingViolations()
        {
            int retries = 0;
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment(
                retryDelay: milliseconds =>
                {
                    if (++retries > 200)
                    {
                        throw new TimeoutException("Cleanup exceeded the test retry limit.");
                    }

                    Thread.Sleep(milliseconds);
                }))
            {
                string lockedDirectory = Path.Combine(environment.RootDirectory, "locked");
                Directory.CreateDirectory(lockedDirectory);
                string childFile = Path.Combine(lockedDirectory, "child.txt");
                File.WriteAllText(childFile, "cleanup");
                using (SafeFileHandle directoryLock = OpenDirectoryWithoutDeleteSharing(lockedDirectory))
                {
                    IOException exception = Assert.Throws<IOException>(() => environment.Dispose());
                    Assert.That(exception.HResult, Is.EqualTo(unchecked((int)0x80070020)));
                    Assert.That(retries, Is.GreaterThan(0));
                    Assert.That(Directory.Exists(environment.RootDirectory), Is.True);
                    Assert.That(File.Exists(childFile), Is.False);
                }

                environment.Dispose();
                Assert.That(Directory.Exists(environment.RootDirectory), Is.False);
            }
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DisposeDoesNotRetryOtherIOExceptions()
        {
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment(
                retryDelay: _ => Assert.Fail("Only sharing violations can retry cleanup.")))
            {
                Directory.Delete(environment.RootDirectory);
                try
                {
                    Assert.Throws<DirectoryNotFoundException>(() => environment.Dispose());
                }
                finally
                {
                    Directory.CreateDirectory(environment.RootDirectory);
                }
            }
        }

        [Test]
        public async Task ConcurrentGitReadsKeepSeparateConfigurationAndLeaveTheParentUnchanged()
        {
            Dictionary<string, string> parent = SnapshotEnvironment();
            string firstRoot;
            string secondRoot;
            using (IsolatedGitEnvironment first = new IsolatedGitEnvironment())
            using (IsolatedGitEnvironment second = new IsolatedGitEnvironment())
            using (CountdownEvent started = new CountdownEvent(2))
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            {
                firstRoot = first.RootDirectory;
                secondRoot = second.RootDirectory;
                File.WriteAllText(Path.Combine(firstRoot, ".gitconfig"), "[isolation]\nvalue = first\n");
                File.WriteAllText(Path.Combine(secondRoot, ".gitconfig"), "[isolation]\nvalue = second\n");
                BarrierGitProcess firstGit = new BarrierGitProcess(first, started, release);
                BarrierGitProcess secondGit = new BarrierGitProcess(second, started, release);
                Task<GitProcess.ConfigResult> firstRead = Task.Run(() => firstGit.GetFromConfig("isolation.value", forceOutsideEnlistment: true));
                Task<GitProcess.ConfigResult> secondRead = Task.Run(() => secondGit.GetFromConfig("isolation.value", forceOutsideEnlistment: true));
                try
                {
                    Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True, "Both Git processes must reach the launch boundary.");
                    AssertParentEnvironment(parent);
                }
                finally
                {
                    release.Set();
                    try
                    {
                        await Task.WhenAll(firstRead, secondRead).WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    catch (TimeoutException)
                    {
                        string firstStopError = StopGit(firstGit);
                        string secondStopError = StopGit(secondGit);
                        await Task.WhenAll(firstRead, secondRead).WaitAsync(TimeSpan.FromSeconds(10));
                        Assert.That(firstStopError, Is.Null, firstStopError);
                        Assert.That(secondStopError, Is.Null, secondStopError);
                        throw;
                    }
                }

                Assert.That(firstRead.Result.TryParseAsString(out string firstValue, out string firstError), Is.True, firstError);
                Assert.That(secondRead.Result.TryParseAsString(out string secondValue, out string secondError), Is.True, secondError);
                Assert.That(firstValue, Is.EqualTo("first"));
                Assert.That(secondValue, Is.EqualTo("second"));
                AssertParentEnvironment(parent);
            }

            Assert.That(Directory.Exists(firstRoot), Is.False);
            Assert.That(Directory.Exists(secondRoot), Is.False);
            AssertParentEnvironment(parent);
        }

        [Test]
        public void ApplyRemovesInheritedGitConfigurationWithoutChangingTheParent()
        {
            Dictionary<string, string> parent = SnapshotEnvironment();
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment())
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(Settings.Default.PathToGit);
                string[] inherited =
                {
                    "GIT_CONFIG", "GIT_CONFIG_COUNT", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM",
                    "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0",
                    "GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE",
                };
                foreach (string key in inherited)
                {
                    startInfo.Environment[key] = "inherited-test-value";
                }

                startInfo.Environment["HOME"] = "inherited-home";
                startInfo.Environment["XDG_CONFIG_HOME"] = "inherited-xdg-home";
                startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "0";
                startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
                startInfo.Environment["GCM_VALIDATE"] = "0";
                environment.Apply(startInfo);

                foreach (string key in inherited)
                {
                    Assert.That(startInfo.Environment.ContainsKey(key), Is.False, key);
                }

                Assert.That(startInfo.Environment["HOME"], Is.EqualTo(environment.RootDirectory));
                Assert.That(startInfo.Environment["XDG_CONFIG_HOME"], Is.EqualTo(Path.Combine(environment.RootDirectory, "xdg")));
                Assert.That(startInfo.Environment["GIT_CONFIG_NOSYSTEM"], Is.EqualTo("1"));
                Assert.That(startInfo.Environment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
                Assert.That(startInfo.Environment["GCM_VALIDATE"], Is.EqualTo("0"));
                AssertParentEnvironment(parent);
            }

            AssertParentEnvironment(parent);
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailedProcessStartDoesNotChangeTheParentOrLeakTheTemporaryDirectory()
        {
            Dictionary<string, string> parent = SnapshotEnvironment();
            string root;
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment())
            {
                root = environment.RootDirectory;
                using Process process = environment.CreateGitProcess(Path.Combine(root, "missing-git.exe"), root)
                    .GetGitProcess("--version", root, null, false, null, false);
                Assert.Throws<Win32Exception>(() => process.Start());
                AssertParentEnvironment(parent);
            }

            Assert.That(Directory.Exists(root), Is.False);
            AssertParentEnvironment(parent);
        }

        [Test]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task CanceledReadDoesNotChangeTheParentOrLeakTheTemporaryDirectory()
        {
            Dictionary<string, string> parent = SnapshotEnvironment();
            string root;
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                root = environment.RootDirectory;
                int attempts = 0;
                TaskCompletionSource<Process> created = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<GitProcess.Result> read = Task.Run(() => GitProcessConfigReader.ReadRaw(
                    "isolation",
                    () =>
                    {
                        ++attempts;
                        Process process = environment.CreateGitProcess(Settings.Default.PathToGit, root)
                            .GetGitProcess("hash-object --stdin", root, null, false, null, false);
                        process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "PING.EXE");
                        process.StartInfo.Arguments = "-n 30 127.0.0.1";
                        created.SetResult(process);
                        return process;
                    },
                    string.Empty,
                    cancellation.Token,
                    tracer: null));
                try
                {
                    Process process = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    int processId = 0;
                    Assert.That(SpinWait.SpinUntil(() => TryGetStartedProcessId(process, out processId), TimeSpan.FromSeconds(5)), Is.True);
                    using Process observer = Process.GetProcessById(processId);
                    _ = observer.Handle;
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
                    await observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.That(attempts, Is.EqualTo(1));
                }
                finally
                {
                    cancellation.Cancel();
                    try
                    {
                        await read.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                }

                AssertParentEnvironment(parent);
            }

            Assert.That(Directory.Exists(root), Is.False);
            AssertParentEnvironment(parent);
        }

        private static SafeFileHandle OpenDirectoryWithoutDeleteSharing(string path)
        {
            const uint FileFlagBackupSemantics = 0x02000000;
            SafeFileHandle handle = NativeMethods.CreateFile(
                path,
                (uint)FileAccess.Read,
                FileShare.Read | FileShare.Write,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Could not open the directory handle for the cleanup test.");
            }

            return handle;
        }

        private static bool TryGetStartedProcessId(Process process, out int processId)
        {
            try
            {
                processId = process.Id;
                return true;
            }
            catch (InvalidOperationException)
            {
                processId = 0;
                return false;
            }
        }

        private static string StopGit(GitProcess git)
        {
            try
            {
                return git.TryKillRunningProcess(out _, out _, out string error)
                    ? null
                    : error ?? "Could not stop the owned Git process.";
            }
            catch (Exception exception)
            {
                return exception.ToString();
            }
        }

        private static Dictionary<string, string> SnapshotEnvironment()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string variable in new[]
            {
                "HOME", "XDG_CONFIG_HOME", "GIT_CONFIG", "GIT_CONFIG_COUNT", "GIT_CONFIG_GLOBAL",
                "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_SYSTEM",
                "GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE",
            })
            {
                values.Add(variable, Environment.GetEnvironmentVariable(variable));
            }

            return values;
        }

        private static void AssertParentEnvironment(Dictionary<string, string> expected)
        {
            Assert.That(SnapshotEnvironment(), Is.EquivalentTo(expected));
        }

        private sealed class BarrierGitProcess : IsolatedGitProcess
        {
            private readonly CountdownEvent started;
            private readonly ManualResetEventSlim release;
            private int firstLaunch = 1;

            public BarrierGitProcess(IsolatedGitEnvironment environment, CountdownEvent started, ManualResetEventSlim release)
                : base(Settings.Default.PathToGit, environment.RootDirectory, environment)
            {
                this.started = started;
                this.release = release;
            }

            public override Process GetGitProcess(
                string command, string workingDirectory, string dotGitDirectory, bool useReadObjectHook,
                string gitObjectsDirectory, bool usePreCommandHook, bool mayRequireAuth = false, bool? hasVisibleConsoleWindow = null)
            {
                Process process = base.GetGitProcess(
                    command, workingDirectory, dotGitDirectory, useReadObjectHook, gitObjectsDirectory,
                    usePreCommandHook, mayRequireAuth, hasVisibleConsoleWindow);
                if (Interlocked.Exchange(ref this.firstLaunch, 0) != 0)
                {
                    this.started.Signal();
                    if (!this.release.Wait(TimeSpan.FromSeconds(15)))
                    {
                        process.Dispose();
                        throw new TimeoutException("The test did not release the Git processes.");
                    }
                }

                return process;
            }
        }
    }
}
