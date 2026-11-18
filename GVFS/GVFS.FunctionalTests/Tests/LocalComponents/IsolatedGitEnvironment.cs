using System;
using GVFS.Common.Git;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    internal sealed class IsolatedGitEnvironment : IDisposable
    {
        private const int SharingViolationHResult = unchecked((int)0x80070020);
        private const int CleanupTimeoutMs = 5000;
        private const int CleanupRetryDelayMs = 50;

        private readonly Action<int> retryDelay;
        private bool disposed;

        public IsolatedGitEnvironment(Action<int> retryDelay = null)
        {
            this.retryDelay = retryDelay ?? Thread.Sleep;
            this.RootDirectory = Path.Combine(Path.GetTempPath(), "LocalComponents_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.RootDirectory);
        }

        public string RootDirectory { get; }

        public void Apply(ProcessStartInfo startInfo)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            foreach (string key in startInfo.Environment.Keys.Where(key =>
                key.StartsWith("GIT_CONFIG", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                startInfo.Environment.Remove(key);
            }

            startInfo.Environment.Remove("GIT_DIR");
            startInfo.Environment.Remove("GIT_COMMON_DIR");
            startInfo.Environment.Remove("GIT_WORK_TREE");
            startInfo.Environment["HOME"] = this.RootDirectory;
            startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(this.RootDirectory, "xdg");
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        }

        public IsolatedGitProcess CreateGitProcess(string gitPath, string workingDirectory)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            return new IsolatedGitProcess(gitPath, workingDirectory, this);
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            IOException lastSharingViolation = null;
            int retries = 0;
            while (true)
            {
                try
                {
                    Directory.Delete(this.RootDirectory, recursive: true);
                    break;
                }
                catch (IOException exception) when (
                    exception.HResult == SharingViolationHResult &&
                    timer.ElapsedMilliseconds < CleanupTimeoutMs)
                {
                    lastSharingViolation = exception;
                    ++retries;
                    this.retryDelay(CleanupRetryDelayMs);
                }
            }

            this.disposed = true;
            if (lastSharingViolation != null)
            {
                Console.WriteLine(
                    "Temporary directory cleanup recovered after {0} retries in {1} ms: {2}",
                    retries,
                    timer.ElapsedMilliseconds,
                    lastSharingViolation);
            }
        }
    }

    internal class IsolatedGitProcess : GitProcess
    {
        private readonly IsolatedGitEnvironment environment;

        public IsolatedGitProcess(string gitPath, string workingDirectory, IsolatedGitEnvironment environment)
            : base(gitPath, workingDirectory)
        {
            this.environment = environment;
        }

        public override Process GetGitProcess(
            string command,
            string workingDirectory,
            string dotGitDirectory,
            bool useReadObjectHook,
            string gitObjectsDirectory,
            bool usePreCommandHook,
            bool mayRequireAuth = false,
            bool? hasVisibleConsoleWindow = null)
        {
            Process process = base.GetGitProcess(
                command, workingDirectory, dotGitDirectory, useReadObjectHook, gitObjectsDirectory,
                usePreCommandHook, mayRequireAuth, hasVisibleConsoleWindow);
            try
            {
                this.environment.Apply(process.StartInfo);
                return process;
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }
    }
}
