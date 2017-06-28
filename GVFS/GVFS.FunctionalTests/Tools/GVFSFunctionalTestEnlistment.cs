using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tests;
using System;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSFunctionalTestEnlistment
    {
        private const string ZeroBackgroundOperations = "Background operations: 0\r\n";
        private const string LockHeldByGit = "GVFS Lock: Held by {0}";
        private const int SleepMSWaitingForStatusCheck = 100;
        private const int DefaultMaxWaitMSForStatusCheck = 5000;

        private GVFSProcess gvfsProcess;

        private GVFSFunctionalTestEnlistment(string pathToGVFS, string enlistmentRoot, string repoUrl, string commitish)
        {
            this.EnlistmentRoot = Path.Combine(enlistmentRoot, Guid.NewGuid().ToString("N"));
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;

            this.gvfsProcess = new GVFSProcess(pathToGVFS, this.EnlistmentRoot);
        }

        public string EnlistmentRoot
        {
            get; private set;
        }

        public string RepoUrl
        {
            get; private set;
        }

        public string RepoRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, "src"); }
        }

        public string DotGVFSRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, ".gvfs"); }
        }

        public string GVFSLogsRoot
        {
            get { return Path.Combine(this.DotGVFSRoot, "logs"); }
        }

        public string DiagnosticsRoot
        {
            get { return Path.Combine(this.DotGVFSRoot, "diagnostics"); }
        }

        public string Commitish
        {
            get; private set;
        }

        public static GVFSFunctionalTestEnlistment CloneAndMount(string pathToGvfs, string commitish = null)
        {
            GVFSFunctionalTestEnlistment enlistment = new GVFSFunctionalTestEnlistment(
                pathToGvfs,
                Properties.Settings.Default.EnlistmentRoot,
                Properties.Settings.Default.RepoToClone,
                commitish == null ? Properties.Settings.Default.Commitish : commitish);

            try
            {
                enlistment.UnmountAndDeleteAll();
                enlistment.CloneAndMount();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unhandled exception in CloneAndMount: " + e.ToString());
                TestResultsHelper.OutputGVFSLogs(enlistment);
                throw;
            }

            return enlistment;
        }

        public void DeleteEnlistment()
        {
            TestResultsHelper.OutputGVFSLogs(this);

            // Use cmd.exe to delete the enlistment as it properly handles tombstones
            // and reparse points
            CmdRunner cmdRunner = new CmdRunner();
            bool enlistmentExists = Directory.Exists(this.EnlistmentRoot);
            int retryCount = 0;
            while (enlistmentExists)
            {
                string output = cmdRunner.DeleteDirectory(this.EnlistmentRoot);
                enlistmentExists = Directory.Exists(this.EnlistmentRoot);
                if (enlistmentExists)
                {
                    ++retryCount;
                    Thread.Sleep(500);
                    try
                    {                        
                        if (retryCount > 10)
                        {
                            retryCount = 0;
                            throw new DeleteFolderFailedException(output);
                        }
                    }
                    catch (DeleteFolderFailedException)
                    {
                        // Throw\catch a DeleteFolderFailedException here so that developers who are running the tests
                        // and have a debugger attached can be alerted that something is wrong 
                    }
                }
            }
        }

        public void CloneAndMount()
        {
            GVFSServiceProcess.StartService();
            this.gvfsProcess.Clone(this.RepoUrl, this.Commitish);

            this.MountGVFS();
            GitProcess.Invoke(this.RepoRoot, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RepoRoot, "branch --unset-upstream");
            GitProcess.Invoke(this.RepoRoot, "config core.abbrev 40");
            GitProcess.Invoke(this.RepoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RepoRoot, "config user.email \"functional@test.com\"");
        }

        public void MountGVFS()
        {
            this.gvfsProcess.Mount();
        }

        public bool TryMountGVFS()
        {
            string output;
            return this.gvfsProcess.TryMount(out output);
        }

        public string PrefetchFolder(string folderPath)
        {
            return this.gvfsProcess.Prefetch(folderPath);
        }

        public string PrefetchFolderBasedOnFile(string filterFilePath)
        {
            return this.gvfsProcess.PrefetchFolderBasedOnFile(filterFilePath);
        }

        public void Repair()
        {
            this.gvfsProcess.Repair();
        }

        public string Diagnose()
        {
            return this.gvfsProcess.Diagnose();
        }

        public string Status()
        {
            return this.gvfsProcess.Status();
        }

        public bool WaitForBackgroundOperations(int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, ZeroBackgroundOperations);
        }

        public bool WaitForLock(string lockCommand, int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, string.Format(LockHeldByGit, lockCommand));
        }

        public void UnmountGVFS()
        {
            this.gvfsProcess.Unmount();
        }

        public void UnmountAndDeleteAll()
        {
            this.UnmountGVFS();
            this.DeleteEnlistment();
            GVFSServiceProcess.StopService();
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

        private class DeleteFolderFailedException : Exception
        {
            public DeleteFolderFailedException(string message)
                : base(message)
            {
            }
        }
    }
}
