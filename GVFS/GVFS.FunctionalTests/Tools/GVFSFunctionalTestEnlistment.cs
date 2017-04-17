using System;
using System.Collections.Generic;
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

        public GVFSFunctionalTestEnlistment(string pathToGVFS, string enlistmentRoot, string repoUrl, string commitish)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;

            this.gvfsProcess = new GVFSProcess(pathToGVFS, this.EnlistmentRoot);
        }

        private enum BreadcrumbType
        {
            Invalid = 0,

            Restart,

            BeginRecurseIntoDirectory,
            EndRecurseIntoDirectory,

            TryDeleteFile,
            TryDeleteEmptyDirectory,

            DeleteSucceeded,
            DeleteFailed,

            SilentFailure,
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

        public static GVFSFunctionalTestEnlistment Create(string pathToGvfs)
        {
            return new GVFSFunctionalTestEnlistment(
                pathToGvfs,
                Properties.Settings.Default.EnlistmentRoot,
                Properties.Settings.Default.RepoToClone,
                Properties.Settings.Default.Commitish);
        }

        public static GVFSFunctionalTestEnlistment CloneAndMount(string pathToGvfs)
        {
            GVFSFunctionalTestEnlistment enlistment = GVFSFunctionalTestEnlistment.Create(pathToGvfs);
            enlistment.UnmountAndDeleteAll();
            enlistment.CloneAndMount();

            return enlistment;
        }

        public void DeleteEnlistment()
        {
            if (Directory.Exists(this.EnlistmentRoot))
            {
                List<Breadcrumb> breadcrumbs = new List<Breadcrumb>();
                RecursiveFolderDeleteRetryForever(breadcrumbs, this.EnlistmentRoot);
            }
        }

        public void CloneAndMount()
        {
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

        public string PrefetchFolder(string folderPath)
        {
            return this.gvfsProcess.Prefetch(folderPath);
        }

        public string PrefetchFolderBasedOnFile(string filterFilePath)
        {
            return this.gvfsProcess.PrefetchFolderBasedOnFile(filterFilePath);
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
            try
            {
                this.UnmountGVFS();
            }
            finally
            {
                this.DeleteEnlistment();
            }
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

        private static void RecursiveFolderDeleteRetryForever(List<Breadcrumb> breadcrumbs, string path)
        {
            while (true)
            {
                if (TryRecursiveFolderDelete(breadcrumbs, path))
                {
                    return;
                }

                breadcrumbs.Add(new Breadcrumb(BreadcrumbType.Restart, null));
                Thread.Sleep(500);
            }
        }

        private static bool TryRecursiveFolderDelete(List<Breadcrumb> breadcrumbs, string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            breadcrumbs.Add(new Breadcrumb(BreadcrumbType.BeginRecurseIntoDirectory, path));

            try
            {
                try
                {
                    foreach (FileInfo file in directory.GetFiles())
                    {
                        try
                        {
                            file.Attributes = FileAttributes.Normal;
                        }
                        catch (ArgumentException)
                        {
                            // Setting the attributes will throw an ArgumentException in situations where
                            // it really ought to throw IOExceptions, e.g. because the file is currently locked
                            return false;
                        }

                        if (!TryDelete(breadcrumbs, file))
                        {
                            return false;
                        }
                    }

                    foreach (DirectoryInfo subDirectory in directory.GetDirectories())
                    {
                        if (!TryRecursiveFolderDelete(breadcrumbs, subDirectory.FullName))
                        {
                            return false;
                        }
                    }
                }
                catch (DirectoryNotFoundException e)
                {
                    // For junctions directory.GetFiles() or .GetDirectories() can throw DirectoryNotFoundException
                    breadcrumbs.Add(new Breadcrumb(BreadcrumbType.DeleteFailed, path, e));
                }
                catch (IOException e)
                {
                    // There is a race when enumerating while a virtualization instance is being shut down
                    // If GVFlt receives the enumeration request before it knows that GVFS has been shut down
                    // (and then GVFS does not handle the request because it is shut down) we can get an IOException
                    breadcrumbs.Add(new Breadcrumb(BreadcrumbType.DeleteFailed, path, e));
                    return false;
                }

                if (!TryDelete(breadcrumbs, directory))
                {
                    return false;
                }

                return true;
            }
            finally
            {
                breadcrumbs.Add(new Breadcrumb(BreadcrumbType.EndRecurseIntoDirectory, path));
            }
        }

        private static bool TryDelete(List<Breadcrumb> breadcrumbs, FileSystemInfo fileOrFolder)
        {
            bool isFile = fileOrFolder is FileInfo;

            breadcrumbs.Add(new Breadcrumb(
                isFile ? BreadcrumbType.TryDeleteFile : BreadcrumbType.TryDeleteEmptyDirectory,
                fileOrFolder.FullName));

            try
            {
                fileOrFolder.Delete();
                if ((isFile && File.Exists(fileOrFolder.FullName)) ||
                    (!isFile && Directory.Exists(fileOrFolder.FullName)))
                {
                    breadcrumbs.Add(new Breadcrumb(BreadcrumbType.SilentFailure, fileOrFolder.FullName));
                    return false;
                }
                else
                {
                    breadcrumbs.Add(new Breadcrumb(BreadcrumbType.DeleteSucceeded, fileOrFolder.FullName));
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
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

        private class Breadcrumb
        {
            public Breadcrumb(BreadcrumbType type, string path, Exception exception = null)
            {
                this.BreadcrumbType = type;
                this.Path = path;
                this.Exception = exception;
            }

            public BreadcrumbType BreadcrumbType { get; private set; }
            public string Path { get; private set; }
            public Exception Exception { get; private set; }

            public override string ToString()
            {
                return this.BreadcrumbType + ":" + this.Path;
            }
        }
    }
}
