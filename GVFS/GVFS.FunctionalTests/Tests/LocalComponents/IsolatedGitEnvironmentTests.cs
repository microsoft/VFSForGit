using GVFS.Common.Git;
using GVFS.FunctionalTests.Properties;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    public class IsolatedGitEnvironmentTests
    {
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
                    await Task.WhenAll(firstRead, secondRead).WaitAsync(TimeSpan.FromSeconds(30));
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
        public void CanceledReadDoesNotChangeTheParentOrLeakTheTemporaryDirectory()
        {
            Dictionary<string, string> parent = SnapshotEnvironment();
            string root;
            using (IsolatedGitEnvironment environment = new IsolatedGitEnvironment())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (ManualResetEventSlim exited = new ManualResetEventSlim())
            {
                root = environment.RootDirectory;
                int attempts = 0;
                Assert.Catch<OperationCanceledException>(() => GitProcessConfigReader.ReadRaw(
                    "isolation",
                    () =>
                    {
                        ++attempts;
                        Process process = environment.CreateGitProcess(Settings.Default.PathToGit, root)
                            .GetGitProcess("hash-object --stdin", root, null, false, null, false);
                        process.StartInfo.FileName = Environment.GetEnvironmentVariable("ComSpec");
                        process.StartInfo.Arguments = "/d /c ping -n 30 127.0.0.1 >nul";
                        process.EnableRaisingEvents = true;
                        process.Exited += (sender, args) => exited.Set();
                        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));
                        return process;
                    },
                    string.Empty,
                    cancellation.Token,
                    tracer: null));
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(exited.Wait(TimeSpan.FromSeconds(5)), Is.True, "The canceled process must exit before cleanup.");
                AssertParentEnvironment(parent);
            }

            Assert.That(Directory.Exists(root), Is.False);
            AssertParentEnvironment(parent);
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
                this.started.Signal();
                if (!this.release.Wait(TimeSpan.FromSeconds(15)))
                {
                    process.Dispose();
                    throw new TimeoutException("The test did not release the Git processes.");
                }

                return process;
            }
        }
    }
}
