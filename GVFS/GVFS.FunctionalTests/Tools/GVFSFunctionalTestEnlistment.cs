using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            TestResultsHelper.OutputGVFSLogs(this);
            RepositoryHelpers.DeleteTestDirectory(this.EnlistmentRoot);
        }

        public void CloneAndMount(bool skipPrefetch)
        {
            Console.Error.WriteLine("[CI-DEBUG] CloneAndMount: starting clone of " + this.RepoUrl);
            Console.Error.Flush();
            this.gvfsProcess.Clone(this.RepoUrl, this.Commitish, skipPrefetch);
            Console.Error.WriteLine("[CI-DEBUG] CloneAndMount: clone complete, running git checkout " + this.Commitish);
            Console.Error.Flush();

            InvokeGitWithDiagnostics(this.RepoRoot, "checkout " + this.Commitish, timeoutSeconds: 120);
            InvokeGitWithDiagnostics(this.RepoRoot, "branch --unset-upstream", timeoutSeconds: 30);
            InvokeGitWithDiagnostics(this.RepoRoot, "config core.abbrev 40", timeoutSeconds: 10);
            InvokeGitWithDiagnostics(this.RepoRoot, "config user.name \"Functional Test User\"", timeoutSeconds: 10);
            InvokeGitWithDiagnostics(this.RepoRoot, "config user.email \"functional@test.com\"", timeoutSeconds: 10);

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
            this.UnmountGVFS();
            this.DeleteEnlistment();
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

        /// <summary>
        /// Runs a git command with a timeout and dumps diagnostic info if it
        /// hangs. This is a temporary diagnostic wrapper for investigating
        /// the slice 9 CI timeout where git checkout hangs inside CloneAndMount.
        /// </summary>
        private static void InvokeGitWithDiagnostics(string workingDirectory, string command, int timeoutSeconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Console.Error.WriteLine($"[CI-DEBUG] git {command} — starting (timeout={timeoutSeconds}s)");
            Console.Error.Flush();

            ProcessStartInfo startInfo = new ProcessStartInfo(Properties.Settings.Default.PathToGit)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = command,
            };
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.EnvironmentVariables["GIT_TRACE"] = "1";
            startInfo.EnvironmentVariables["GIT_TRACE_PACKET"] = "0";

            using (Process process = new Process())
            {
                string errors = string.Empty;
                process.StartInfo = startInfo;
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        errors += args.Data + "\r\n";
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(timeoutSeconds * 1000);

                if (!exited)
                {
                    Console.Error.WriteLine($"[CI-DEBUG] git {command} — TIMEOUT after {sw.Elapsed.TotalSeconds:F1}s! PID={process.Id}");
                    Console.Error.Flush();
                    DumpHangDiagnostics(process.Id, workingDirectory);

                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    // Dump whatever stderr we captured
                    if (!string.IsNullOrWhiteSpace(errors))
                    {
                        Console.Error.WriteLine($"[CI-DEBUG] git stderr (partial):\n{errors}");
                        Console.Error.Flush();
                    }

                    throw new TimeoutException(
                        $"git {command} timed out after {timeoutSeconds}s in {workingDirectory}.\n" +
                        $"Stderr: {errors}");
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(); // ensure async stderr completes

                Console.Error.WriteLine($"[CI-DEBUG] git {command} — done in {sw.Elapsed.TotalSeconds:F1}s, exit={process.ExitCode}");
                Console.Error.Flush();

                if (!string.IsNullOrWhiteSpace(errors))
                {
                    Console.Error.WriteLine($"[CI-DEBUG] git stderr: {errors.TrimEnd()}");
                    Console.Error.Flush();
                }
            }
        }

        private static void DumpHangDiagnostics(int gitPid, string workingDirectory)
        {
            try
            {
                // List all child processes of the hanging git
                Console.Error.WriteLine("[CI-DEBUG] === HANG DIAGNOSTICS ===");
                Console.Error.Flush();

                // Show git process tree
                ProcessStartInfo wmicInfo = new ProcessStartInfo("wmic")
                {
                    Arguments = $"process where \"ParentProcessId={gitPid}\" get ProcessId,Name,CommandLine /format:list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process wmic = Process.Start(wmicInfo))
                {
                    string wmicOutput = wmic.StandardOutput.ReadToEnd();
                    wmic.WaitForExit(5000);
                    Console.Error.WriteLine($"[CI-DEBUG] Child processes of git (PID {gitPid}):\n{wmicOutput}");
                    Console.Error.Flush();
                }

                // Show all GVFS.Mount processes
                ProcessStartInfo mountInfo = new ProcessStartInfo("wmic")
                {
                    Arguments = "process where \"Name='GVFS.Mount.exe'\" get ProcessId,CommandLine /format:list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process mountProc = Process.Start(mountInfo))
                {
                    string mountOutput = mountProc.StandardOutput.ReadToEnd();
                    mountProc.WaitForExit(5000);
                    Console.Error.WriteLine($"[CI-DEBUG] GVFS.Mount processes:\n{mountOutput}");
                    Console.Error.Flush();
                }

                // Check gvfs status for this enlistment
                string enlistmentRoot = Path.GetDirectoryName(workingDirectory);
                ProcessStartInfo gvfsInfo = new ProcessStartInfo(Properties.Settings.Default.PathToGVFS)
                {
                    Arguments = $"status \"{enlistmentRoot}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process gvfsProc = Process.Start(gvfsInfo))
                {
                    string gvfsOutput = gvfsProc.StandardOutput.ReadToEnd();
                    gvfsProc.WaitForExit(10000);
                    Console.Error.WriteLine($"[CI-DEBUG] gvfs status:\n{gvfsOutput}");
                    Console.Error.Flush();
                }

                // Dump named pipe info — check if mount pipe is responsive
                string pipeName = "GVFS_" + enlistmentRoot.ToUpperInvariant().Replace(":", "_").Replace("\\", "_");
                Console.Error.WriteLine($"[CI-DEBUG] Expected pipe: {pipeName}");

                // List git hook processes
                ProcessStartInfo hookInfo = new ProcessStartInfo("wmic")
                {
                    Arguments = "process where \"Name like '%hook%' or Name like '%virtual-filesystem%'\" get ProcessId,Name,CommandLine /format:list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process hookProc = Process.Start(hookInfo))
                {
                    string hookOutput = hookProc.StandardOutput.ReadToEnd();
                    hookProc.WaitForExit(5000);
                    Console.Error.WriteLine($"[CI-DEBUG] Hook/VFS processes:\n{hookOutput}");
                    Console.Error.Flush();
                }

                Console.Error.WriteLine("[CI-DEBUG] === END HANG DIAGNOSTICS ===");
                Console.Error.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CI-DEBUG] DumpHangDiagnostics failed: {ex.Message}");
                Console.Error.Flush();
            }
        }
    }
}
