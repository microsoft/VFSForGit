using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Tests;
using System;
using System.IO;
using System.Threading;

namespace RGFS.FunctionalTests.Tools
{
    public class RGFSFunctionalTestEnlistment
    {
        private const string ZeroBackgroundOperations = "Background operations: 0\r\n";
        private const string LockHeldByGit = "RGFS Lock: Held by {0}";
        private const int SleepMSWaitingForStatusCheck = 100;
        private const int DefaultMaxWaitMSForStatusCheck = 5000;

        private RGFSProcess rgfsProcess;
        
        private RGFSFunctionalTestEnlistment(string pathToRGFS, string enlistmentRoot, string repoUrl, string commitish)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;           
            this.rgfsProcess = new RGFSProcess(pathToRGFS, this.EnlistmentRoot);
            this.ObjectRoot = Path.Combine(this.DotRGFSRoot, "gitObjectCache");
        }
        
        public string EnlistmentRoot
        {
            get; private set;
        }

        public string RepoUrl
        {
            get; private set;
        }
        
        public string ObjectRoot
        {
            get; private set;
        }

        public string RepoRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, "src"); }
        }

        public string DotRGFSRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, ".rgfs"); }
        }

        public string RGFSLogsRoot
        {
            get { return Path.Combine(this.DotRGFSRoot, "logs"); }
        }

        public string DiagnosticsRoot
        {
            get { return Path.Combine(this.DotRGFSRoot, "diagnostics"); }
        }

        public string Commitish
        {
            get; private set;
        }

        public static string GetUniqueEnlistmentRoot()
        {
            return Path.Combine(Properties.Settings.Default.EnlistmentRoot, Guid.NewGuid().ToString("N"));
        }

        public static RGFSFunctionalTestEnlistment CloneAndMount(string pathToRgfs, string commitish = null)
        {
            string enlistmentRoot = RGFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            return CloneAndMount(pathToRgfs, enlistmentRoot, commitish);
        }

        public void DeleteEnlistment()
        {
            TestResultsHelper.OutputRGFSLogs(this);

            // Use cmd.exe to delete the enlistment as it properly handles tombstones and reparse points
            CmdRunner.DeleteDirectoryWithRetry(this.EnlistmentRoot);
        }

        public void CloneAndMount()
        {
            this.rgfsProcess.Clone(this.RepoUrl, this.Commitish);

            this.MountRGFS();
            GitProcess.Invoke(this.RepoRoot, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RepoRoot, "branch --unset-upstream");
            GitProcess.Invoke(this.RepoRoot, "config core.abbrev 40");
            GitProcess.Invoke(this.RepoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RepoRoot, "config user.email \"functional@test.com\"");
        }

        public void MountRGFS()
        {
            this.rgfsProcess.Mount();
        }

        public bool TryMountRGFS()
        {
            string output;
            return this.TryMountRGFS(out output);
        }

        public bool TryMountRGFS(out string output)
        {
            return this.rgfsProcess.TryMount(out output);
        }

        public string Prefetch(string args)
        {
            return this.rgfsProcess.Prefetch(args);
        }

        public void Repair()
        {
            this.rgfsProcess.Repair();
        }

        public string Diagnose()
        {
            return this.rgfsProcess.Diagnose();
        }

        public string Status()
        {
            return this.rgfsProcess.Status();
        }

        public bool WaitForBackgroundOperations(int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, ZeroBackgroundOperations);
        }

        public bool WaitForLock(string lockCommand, int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, string.Format(LockHeldByGit, lockCommand));
        }

        public void UnmountRGFS()
        {
            this.rgfsProcess.Unmount();
        }

        public string GetCacheServer()
        {
            return this.rgfsProcess.CacheServer("--get");
        }

        public string SetCacheServer(string arg)
        {
            return this.rgfsProcess.CacheServer("--set " + arg);
        }

        public void UnmountAndDeleteAll()
        {
            this.UnmountRGFS();
            this.DeleteEnlistment();
        }

        public string GetVirtualPathTo(string pathInRepo)
        {
            return Path.Combine(this.RepoRoot, pathInRepo);
        }

        public string GetObjectPathTo(string objectHash)
        {
            return Path.Combine(
                this.RepoRoot,
                TestConstants.DotGit.Objects.Root,
                objectHash.Substring(0, 2),
                objectHash.Substring(2));
        }
        
        private static RGFSFunctionalTestEnlistment CloneAndMount(string pathToRgfs, string enlistmentRoot, string commitish)
        {
            RGFSFunctionalTestEnlistment enlistment = new RGFSFunctionalTestEnlistment(
                pathToRgfs,
                enlistmentRoot ?? GetUniqueEnlistmentRoot(),
                RGFSTestConfig.RepoToClone,
                commitish ?? Properties.Settings.Default.Commitish);

            try
            {
                enlistment.CloneAndMount();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unhandled exception in CloneAndMount: " + e.ToString());
                TestResultsHelper.OutputRGFSLogs(enlistment);
                throw;
            }

            return enlistment;
        }

        private bool WaitForStatus(int maxWaitMilliseconds, string statusShouldContain)
        {
            string status = null;
            int totalWaitMilliseconds = 0;
            while (totalWaitMilliseconds <= maxWaitMilliseconds && (status == null || !status.Contains(statusShouldContain)))
            {
                Thread.Sleep(SleepMSWaitingForStatusCheck);
                status = this.Status();
                totalWaitMilliseconds += SleepMSWaitingForStatusCheck;
            }

            return totalWaitMilliseconds <= maxWaitMilliseconds;
        }
    }
}
