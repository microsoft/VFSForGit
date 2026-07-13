using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests;
using GVFS.Tests.Should;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSFunctionalTestEnlistment
    {
        private const string LockHeldByGit = "GVFS Lock: Held by {0}";
        private const int SleepMSWaitingForStatusCheck = 100;
        private const int DefaultMaxWaitMSForStatusCheck = 5000;
        private static readonly string ZeroBackgroundOperations = "Background operations: 0" + Environment.NewLine;

        private GVFSProcess gvfsProcess;

        private GVFSFunctionalTestEnlistment(string pathToGVFS, string enlistmentRoot, string repoUrl, string commitish, string localCacheRoot = null)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;

            if (localCacheRoot == null)
            {
                if (GVFSTestConfig.NoSharedCache)
                {
                    // eg C:\Repos\GVFSFunctionalTests\enlistment\7942ca69d7454acbb45ea39ef5be1d15\.gvfs\.gvfsCache
                    localCacheRoot = GetRepoSpecificLocalCacheRoot(enlistmentRoot);
                }
                else
                {
                    // eg C:\Repos\GVFSFunctionalTests\.gvfsCache
                    // Ensures the general cache is not cleaned up between test runs
                    localCacheRoot = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", ".gvfsCache");
                }
            }

            this.LocalCacheRoot = localCacheRoot;
            this.gvfsProcess = new GVFSProcess(pathToGVFS, this.EnlistmentRoot, this.LocalCacheRoot);
        }

        public string EnlistmentRoot
        {
            get; private set;
        }

        public string RepoUrl
        {
            get; private set;
        }

        public string LocalCacheRoot { get; }

        public string RepoBackingRoot
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return Path.Combine(this.EnlistmentRoot, ".vfsforgit/lower");
                }
                else
                {
                    return this.RepoRoot;
                }
            }
        }

        public string RepoRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, "src"); }
        }

        public string DotGVFSRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, GVFSTestConfig.DotGVFSRoot); }
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

        public static GVFSFunctionalTestEnlistment CloneAndMountWithPerRepoCache(string pathToGvfs, bool skipPrefetch)
        {
            string enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            string localCache = GVFSFunctionalTestEnlistment.GetRepoSpecificLocalCacheRoot(enlistmentRoot);
            return CloneAndMount(pathToGvfs, enlistmentRoot, null, localCache, skipPrefetch);
        }

        public static GVFSFunctionalTestEnlistment CloneAndMount(
            string pathToGvfs,
            string commitish = null,
            string localCacheRoot = null,
            bool skipPrefetch = false)
        {
            string enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            return CloneAndMount(pathToGvfs, enlistmentRoot, commitish, localCacheRoot, skipPrefetch);
        }

        public static GVFSFunctionalTestEnlistment CloneAndMountEnlistmentWithSpacesInPath(string pathToGvfs, string commitish = null)
        {
            string enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRootWithSpaces();
            string localCache = GVFSFunctionalTestEnlistment.GetRepoSpecificLocalCacheRoot(enlistmentRoot);
            return CloneAndMount(pathToGvfs, enlistmentRoot, commitish, localCache);
        }

        public static string GetUniqueEnlistmentRoot()
        {
            return Path.Combine(Properties.Settings.Default.EnlistmentRoot, Guid.NewGuid().ToString("N").Substring(0, 20));
        }

        public static string GetUniqueEnlistmentRootWithSpaces()
        {
            return Path.Combine(Properties.Settings.Default.EnlistmentRoot, "test " + Guid.NewGuid().ToString("N").Substring(0, 15));
        }

        public string GetObjectRoot(FileSystemRunner fileSystem)
        {
            string mappingFile = Path.Combine(this.LocalCacheRoot, "mapping.dat");
            mappingFile.ShouldBeAFile(fileSystem);

            HashSet<string> allowedFileNames = new HashSet<string>(FileSystemHelpers.PathComparer)
            {
                "mapping.dat",
                "mapping.dat.lock" // mapping.dat.lock can be present, but doesn't have to be present
            };

            this.LocalCacheRoot.ShouldBeADirectory(fileSystem).WithFiles().ShouldNotContain(f => !allowedFileNames.Contains(f.Name));

            string mappingFileContents = File.ReadAllText(mappingFile);
            mappingFileContents.ShouldNotBeNull();
            string[] objectRootEntries = mappingFileContents.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                            .Where(x => x.IndexOf(this.RepoUrl, StringComparison.OrdinalIgnoreCase) >= 0)
                                                            .ToArray();
            objectRootEntries.Length.ShouldEqual(1, $"Should be only one entry for repo url: {this.RepoUrl} mapping file content: {mappingFileContents}");
            objectRootEntries[0].Substring(0, 2).ShouldEqual("A ", $"Invalid mapping entry for repo: {objectRootEntries[0]}");
            using (JsonDocument rootEntryJson = JsonDocument.Parse(objectRootEntries[0].Substring(2)))
            {
                string objectRootFolder = rootEntryJson.RootElement.GetProperty("Value").GetString();
                objectRootFolder.ShouldNotBeNull();
                objectRootFolder.Length.ShouldBeAtLeast(1, $"Invalid object root folder: {objectRootFolder} for {this.RepoUrl} mapping file content: {mappingFileContents}");

                return Path.Combine(this.LocalCacheRoot, objectRootFolder, "gitObjects");
            }
        }

        public string GetPackRoot(FileSystemRunner fileSystem)
        {
            return Path.Combine(this.GetObjectRoot(fileSystem), "pack");
        }

        public void DeleteEnlistment()
        {
            this.CaptureFailureLogs();
            TestResultsHelper.OutputGVFSLogs(this);
            RepositoryHelpers.DeleteTestDirectory(this.EnlistmentRoot);
        }

        /// <summary>
        /// When the current test has failed, writes a full-memory minidump of each still-running
        /// GVFS.Mount process for this enlistment, so a mount *hang* can be diagnosed after the fact.
        /// Must be called before the mount is unmounted or killed - once the process is gone (whether
        /// cleanly unmounted or force-killed) there is nothing left to dump. Written under
        /// <see cref="TestResultsHelper.DiagnosticsRoot"/> so CI can upload it. Best-effort: never
        /// throws, so it cannot break teardown.
        /// </summary>
        public void CaptureFailureDiagnostics()
        {
            try
            {
                if (!this.TryGetFailureDiagnosticsFolder(out string destinationFolder))
                {
                    return;
                }

                List<int> mountProcessIds = this.GetMountProcessIds();
                if (mountProcessIds.Count == 0)
                {
                    Console.Error.WriteLine("[DIAGNOSTICS] No live GVFS.Mount process for this enlistment (already exited/crashed)");
                    return;
                }

                Console.Error.WriteLine($"[DIAGNOSTICS] Test failed; capturing mount dump(s) to '{destinationFolder}'");
                Directory.CreateDirectory(destinationFolder);
                foreach (int pid in mountProcessIds)
                {
                    MiniDump.TryWrite(pid, Path.Combine(destinationFolder, $"GVFS.Mount_{pid}.dmp"));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] CaptureFailureDiagnostics failed: {ex.Message}");
            }
        }

        /// <summary>
        /// When the current test has failed, preserves the enlistment's .gvfs/logs folder (robust to
        /// locked / partially-flushed files) under <see cref="TestResultsHelper.DiagnosticsRoot"/>
        /// before the enlistment directory is deleted. Best-effort: never throws.
        /// </summary>
        private void CaptureFailureLogs()
        {
            try
            {
                if (!this.TryGetFailureDiagnosticsFolder(out string destinationFolder))
                {
                    return;
                }

                Console.Error.WriteLine($"[DIAGNOSTICS] Test failed; capturing logs to '{destinationFolder}'");
                TestResultsHelper.CopyFilesWithFallback(this.GVFSLogsRoot, Path.Combine(destinationFolder, "logs"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] CaptureFailureLogs failed: {ex.Message}");
            }
        }

        private bool TryGetFailureDiagnosticsFolder(out string destinationFolder)
        {
            destinationFolder = null;
            if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
            {
                return false;
            }

            destinationFolder = Path.Combine(
                TestResultsHelper.DiagnosticsRoot,
                SanitizeForPath(TestContext.CurrentContext.Test.Name) + "_" + Path.GetFileName(this.EnlistmentRoot));
            return true;
        }

        private static string SanitizeForPath(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "test";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            // Flatten characters that are legal in file names but noisy in NUnit
            // test names (parameterized cases, spaces).
            return name.Replace('(', '_').Replace(')', '_').Replace(' ', '_').Replace(',', '_').Replace('"', '_');
        }

        public void CloneAndMount(bool skipPrefetch)
        {
            Console.Error.WriteLine("[CI-DEBUG] CloneAndMount: starting clone of " + this.RepoUrl);
            Console.Error.Flush();
            this.gvfsProcess.Clone(this.RepoUrl, this.Commitish, skipPrefetch);
            Console.Error.WriteLine("[CI-DEBUG] CloneAndMount: clone complete, running git checkout");
            Console.Error.Flush();

            GitProcess.Invoke(this.RepoRoot, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RepoRoot, "branch --unset-upstream");
            GitProcess.Invoke(this.RepoRoot, "config core.abbrev 40");
            GitProcess.Invoke(this.RepoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RepoRoot, "config user.email \"functional@test.com\"");

            // If this repository has a .gitignore file in the root directory, force it to be
            // hydrated. This is because if the GitStatusCache feature is enabled, it will run
            // a "git status" command asynchronously, which will hydrate the .gitignore file
            // as it reads the ignore rules. Hydrate this file here so that it is consistently
            // hydrated and there are no race conditions depending on when / if it is hydrated
            // as part of an asynchronous status scan to rebuild the GitStatusCache.
            string rootGitIgnorePath = Path.Combine(this.RepoRoot, ".gitignore");
            if (File.Exists(rootGitIgnorePath))
            {
                File.ReadAllBytes(rootGitIgnorePath);
            }
        }

        public bool IsMounted()
        {
            return this.gvfsProcess.IsEnlistmentMounted();
        }

        public void MountGVFS()
        {
            this.gvfsProcess.Mount();
        }

        public bool TryMountGVFS()
        {
            string output;
            return this.TryMountGVFS(out output);
        }

        public bool TryMountGVFS(out string output)
        {
            return this.gvfsProcess.TryMount(out output);
        }

        public string Prefetch(string args, bool failOnError = true, string standardInput = null)
        {
            return this.gvfsProcess.Prefetch(args, failOnError, standardInput);
        }

        public void Repair(bool confirm)
        {
            this.gvfsProcess.Repair(confirm);
        }

        public string Diagnose()
        {
            return this.gvfsProcess.Diagnose();
        }

        public string LooseObjectStep()
        {
            return this.gvfsProcess.LooseObjectStep();
        }

        public string PackfileMaintenanceStep(long? batchSize = null)
        {
            return this.gvfsProcess.PackfileMaintenanceStep(batchSize);
        }

        public string PostFetchStep()
        {
            return this.gvfsProcess.PostFetchStep();
        }

        public string Status(string trace = null)
        {
            return this.gvfsProcess.Status(trace);
        }

        public string Health(string directory = null)
        {
            return this.gvfsProcess.Health(directory);
        }

        public bool WaitForBackgroundOperations(int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, ZeroBackgroundOperations).ShouldBeTrue("Background operations failed to complete.");
        }

        public bool WaitForLock(string lockCommand, int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, string.Format(LockHeldByGit, lockCommand));
        }

        public void WriteConfig(string key, string value)
        {
            this.gvfsProcess.WriteConfig(key, value);
        }

        public void UnmountGVFS()
        {
            this.gvfsProcess.Unmount();
        }

        public string GetCacheServer()
        {
            return this.gvfsProcess.CacheServer("--get");
        }

        public string SetCacheServer(string arg)
        {
            return this.gvfsProcess.CacheServer("--set " + arg);
        }

        public void UnmountAndDeleteAll()
        {
            // Capture the mount dump before unmounting or killing anything - once the mount process is
            // gone (whether it unmounts cleanly or is force-killed below) there is nothing left to dump.
            this.CaptureFailureDiagnostics();

            try
            {
                this.UnmountGVFS();
            }
            catch (TimeoutException)
            {
                // If unmount hangs (e.g., GVFS.Mount stuck after objects root
                // deletion), kill the mount process so teardown can proceed.
                Console.Error.WriteLine("[TEARDOWN] Unmount timed out, killing GVFS.Mount process");
                this.KillMountProcess();
            }

            this.DeleteEnlistment();
        }

        public void KillMountProcess()
        {
            foreach (int pid in this.GetMountProcessIds())
            {
                Console.Error.WriteLine($"[TEARDOWN] Killing GVFS.Mount (PID {pid}) for {this.EnlistmentRoot}");
                try
                {
                    System.Diagnostics.Process.GetProcessById(pid)?.Kill();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[TEARDOWN] Failed to kill PID {pid}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Returns the process ids of the GVFS.Mount processes whose command line
        /// references this enlistment root. Uses PowerShell's Get-CimInstance to
        /// read command lines without requiring System.Management. Best-effort:
        /// returns an empty list on any failure (e.g. non-Windows).
        /// </summary>
        private List<int> GetMountProcessIds()
        {
            List<int> processIds = new List<int>();

            try
            {
                // Match on the enlistment's unique leaf folder id rather than the
                // full path. PowerShell's -like treats '\' as a literal (not an
                // escape), so doubling backslashes in the full path would produce a
                // pattern that never matches a real (single-backslash) command line.
                // The leaf id is unique and free of path separators and wildcard
                // metacharacters, so it needs no escaping.
                string filter = Path.GetFileName(this.EnlistmentRoot.TrimEnd('\\', '/'));
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='GVFS.Mount.exe'\\\" | Where-Object {{ $_.CommandLine -like '*{filter}*' }} | ForEach-Object {{ $_.ProcessId }}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var output = new System.Text.StringBuilder();

                // Read output asynchronously via the event, rather than a blocking ReadToEnd() before
                // WaitForExit(): ReadToEnd() blocks until the process closes its stdout handle, so if the
                // helper itself hangs, the later WaitForExit(10000) timeout is never reached at all. With
                // async reads, WaitForExit is the only blocking call, so it enforces a real timeout and we
                // can kill the helper if it does not exit in time.
                using (var proc = new System.Diagnostics.Process { StartInfo = psi })
                {
                    proc.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            output.AppendLine(args.Data);
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();

                    if (!proc.WaitForExit(10000))
                    {
                        Console.Error.WriteLine("[TEARDOWN] GetMountProcessIds helper timed out; killing it");
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                        }
                        catch (Exception killEx)
                        {
                            Console.Error.WriteLine($"[TEARDOWN] Failed to kill GetMountProcessIds helper: {killEx.Message}");
                        }
                    }
                }

                foreach (string line in output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(line.Trim(), out int pid))
                    {
                        processIds.Add(pid);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TEARDOWN] GetMountProcessIds failed: {ex.Message}");
            }

            return processIds;
        }

        public string GetVirtualPathTo(string path)
        {
            // Replace '/' with Path.DirectorySeparatorChar to ensure that any
            // Git paths are converted to system paths
            return Path.Combine(this.RepoRoot, path.Replace(TestConstants.GitPathSeparator, Path.DirectorySeparatorChar));
        }

        public string GetVirtualPathTo(params string[] pathParts)
        {
            return Path.Combine(this.RepoRoot, Path.Combine(pathParts));
        }

        public string GetBackingPathTo(string path)
        {
            // Replace '/' with Path.DirectorySeparatorChar to ensure that any
            // Git paths are converted to system paths
            return Path.Combine(this.RepoBackingRoot, path.Replace(TestConstants.GitPathSeparator, Path.DirectorySeparatorChar));
        }

        public string GetBackingPathTo(params string[] pathParts)
        {
            return Path.Combine(this.RepoBackingRoot, Path.Combine(pathParts));
        }

        public string GetDotGitPath(params string[] pathParts)
        {
            return this.GetBackingPathTo(TestConstants.DotGit.Root, Path.Combine(pathParts));
        }

        public string GetObjectPathTo(string objectHash)
        {
            return Path.Combine(
                this.RepoBackingRoot,
                TestConstants.DotGit.Objects.Root,
                objectHash.Substring(0, 2),
                objectHash.Substring(2));
        }

        private static GVFSFunctionalTestEnlistment CloneAndMount(string pathToGvfs, string enlistmentRoot, string commitish, string localCacheRoot, bool skipPrefetch = false)
        {
            GVFSFunctionalTestEnlistment enlistment = new GVFSFunctionalTestEnlistment(
                pathToGvfs,
                enlistmentRoot ?? GetUniqueEnlistmentRoot(),
                GVFSTestConfig.RepoToClone,
                commitish ?? Properties.Settings.Default.Commitish,
                localCacheRoot ?? GVFSTestConfig.LocalCacheRoot);

            try
            {
                enlistment.CloneAndMount(skipPrefetch);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unhandled exception in CloneAndMount: " + e.ToString());
                TestResultsHelper.OutputGVFSLogs(enlistment);
                throw;
            }

            return enlistment;
        }

        private static string GetRepoSpecificLocalCacheRoot(string enlistmentRoot)
        {
            return Path.Combine(enlistmentRoot, GVFSTestConfig.DotGVFSRoot, ".gvfsCache");
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
