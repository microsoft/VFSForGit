using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [NonParallelizable]
    public class PrefetchVerbTests : TestsWithEnlistmentPerFixture
    {
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";
        private const string LsTreeTypeInPathBranchName = "FunctionalTests/20201014_LsTreeTypeInPath";

        // on case-insensitive filesystems, test case-blind matching in
        // folder lists using "gvfs/" instead of "GVFS/"
        private static readonly string PrefetchGVFSFolder = FileSystemHelpers.CaseSensitiveFileSystem ? "GVFS" : "gvfs";
        private static readonly string PrefetchGVFSFolderPath = PrefetchGVFSFolder + "/";
        private static readonly string[] PrefetchFolderList = new string[]
        {
            "# A comment",
            " ",
            PrefetchGVFSFolderPath, // "GVFS/" or "gvfs/"
            PrefetchGVFSFolderPath + PrefetchGVFSFolder, // "GVFS/GVFS" or "gvfs/gvfs"
            PrefetchGVFSFolderPath,
        };

        private FileSystemRunner fileSystem;

        public PrefetchVerbTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public async Task StalledPrefetchHonorsConfiguredReadDeadline()
        {
            string[] keys = new[]
            {
                "gvfs.cache-server", "gvfs.prefetch.cache-server",
                "gvfs.read-timeout-seconds", "gvfs.max-retries",
            };
            Dictionary<string, string> previous = this.ReadLocalConfig(keys);

            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string endpoint = $"http://127.0.0.1:{port}";
            Task server = ServeStalledPrefetch(listener, cancellation.Token);
            try
            {
                this.SetLocalConfig(keys[0], endpoint);
                this.SetLocalConfig(keys[1], endpoint);
                this.SetLocalConfig(keys[2], "1");
                this.SetLocalConfig(keys[3], "0");

                ProcessResult result = await Task.Run(() => ProcessHelper.Run(
                    GVFSTestConfig.PathToGVFS,
                    "prefetch --commits",
                    this.Enlistment.RepoRoot,
                    timeoutMs: 20000));
                result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
                string log = Directory.GetFiles(Path.Combine(this.Enlistment.DotGVFSRoot, "logs"), "gvfs_prefetch_*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(File.ReadAllText)
                    .First();
                log.ShouldContain("NetworkBodyReadTimeout");
                log.ShouldContain("\"ReadTimeoutMs\":1000");
                log.ShouldContain("\"Outcome\":\"TimedOut\"");
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                finally
                {
                    this.RestoreLocalConfig(previous);
                }
            }
        }

        [TestCase]
        public void InvalidRetrySettingsDoNotStopAMountedEnlistment()
        {
            Dictionary<string, string> previous = this.ReadLocalConfig(new[]
            {
                "gvfs.max-retries", "gvfs.timeout-seconds", "gvfs.read-timeout-seconds",
            });
            this.Enlistment.IsMounted().ShouldEqual(true);
            try
            {
                foreach (string key in previous.Keys)
                {
                    this.SetLocalConfig(key, "invalid");
                }

                ProcessResult result = ProcessHelper.Run(
                    GVFSTestConfig.PathToGVFS,
                    "prefetch --commits",
                    this.Enlistment.RepoRoot,
                    timeoutMs: 30000);
                result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
                this.Enlistment.IsMounted().ShouldEqual(true);
                string log = Directory.GetFiles(Path.Combine(this.Enlistment.DotGVFSRoot, "logs"), "gvfs_prefetch_*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(File.ReadAllText)
                    .First();
                log.ShouldContain("VFS for Git uses the default value.");
                foreach (string key in previous.Keys)
                {
                    log.ShouldContain("\"ConfigName\":\"" + key + "\"");
                }
            }
            finally
            {
                this.RestoreLocalConfig(previous);
            }
        }

        private Dictionary<string, string> ReadLocalConfig(IEnumerable<string> keys)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (string key in keys)
            {
                ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local --get " + key);
                (result.ExitCode == 0 || result.ExitCode == 1).ShouldEqual(true, result.Errors);
                values[key] = result.ExitCode == 0 ? result.Output.TrimEnd('\r', '\n') : null;
            }

            return values;
        }

        private void RestoreLocalConfig(Dictionary<string, string> values)
        {
            foreach (KeyValuePair<string, string> setting in values)
            {
                if (setting.Value == null)
                {
                    ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local --unset-all " + setting.Key);
                    (result.ExitCode == 0 || result.ExitCode == 5).ShouldEqual(true, result.Errors);
                }
                else
                {
                    this.SetLocalConfig(setting.Key, setting.Value);
                }
            }
        }

        private void SetLocalConfig(string key, string value)
        {
            ProcessResult result = GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot, $"config --local {key} \"{value}\"");
            result.ExitCode.ShouldEqual(0, result.Errors);
        }

        private static async Task ServeStalledPrefetch(TcpListener listener, CancellationToken cancellationToken)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = client.GetStream();
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
            {
                string line;
                do
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                while (!string.IsNullOrEmpty(line));
            }

            byte[] headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/x-gvfs-timestamped-packfiles-indexes\r\nContent-Length: 1024\r\n\r\n");
            await stream.WriteAsync(headers.AsMemory(), cancellationToken);
            await stream.WriteAsync(new byte[] { (byte)'G' }.AsMemory(), cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        [SetUp]
        public void DeletePrefetchCache()
        {
            string cachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "BlobPrefetchCache.dat");
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
        }

        [TestCase, Order(1)]
        public void PrefetchAllMustBeExplicit()
        {
            this.Enlistment.Prefetch(string.Empty, failOnError: false).ShouldContain("Did you mean to fetch all blobs?");
        }

        [TestCase, Order(2)]
        public void PrefetchSpecificFiles()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")}"), 1);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")};{Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")}"), 2);
        }

        [TestCase, Order(3)]
        public void PrefetchByFileExtension()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs"), 199);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs;*.csproj"), 208);
        }

        [TestCase, Order(4)]
        public void PrefetchByFileExtensionWithHydrate()
        {
            int expectedCount = 3;
            string output = this.Enlistment.Prefetch("--files *.md --hydrate");
            this.ExpectBlobCount(output, expectedCount);
            output.ShouldContain("Hydrated files:   " + expectedCount);
        }

        [TestCase, Order(5)]
        public void PrefetchByFilesWithHydrateWhoseObjectsAreAlreadyDownloaded()
        {
            int expectedCount = 2;
            string output = this.Enlistment.Prefetch(
                $"--files {Path.Combine("GVFS", "GVFS", "Program.cs")};{Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")} --hydrate");
            this.ExpectBlobCount(output, expectedCount);
            output.ShouldContain("Hydrated files:   " + expectedCount);
            output.ShouldContain("Downloaded:       0");
        }

        [TestCase, Order(6)]
        public void PrefetchFolders()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.Combine("GVFS", "GVFS")}"), 17);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.Combine("GVFS", "GVFS")};{Path.Combine("GVFS", "GVFS.FunctionalTests")}"), 65);
        }

        [TestCase, Order(7)]
        public void PrefetchIsAllowedToDoNothing()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files nonexistent.txt"), 0);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders nonexistent_folder"), 0);
        }

        [TestCase, Order(8)]
        public void PrefetchFolderListFromFile()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            File.WriteAllLines(tempFilePath, PrefetchFolderList);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders-list \"" + tempFilePath + "\""), 279);
            File.Delete(tempFilePath);
        }

        [TestCase, Order(9)]
        public void PrefetchAll()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.DirectorySeparatorChar}"), 494);
        }

        [TestCase, Order(10)]
        public void NoopPrefetch()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);

            this.Enlistment.Prefetch("--files *").ShouldContain("Nothing new to prefetch.");
        }

        [TestCase, Order(11)]
        public void PrefetchCleansUpStalePrefetchLock()
        {
            this.Enlistment.Prefetch("--commits");
            this.PostFetchStepShouldComplete();
            string prefetchCommitsLockFile = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), "pack", PrefetchCommitsAndTreesLock);
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(prefetchCommitsLockFile, this.Enlistment.EnlistmentRoot);
            prefetchCommitsLockFile.ShouldBeAFile(this.fileSystem);

            this.fileSystem
                .EnumerateDirectory(this.Enlistment.GetPackRoot(this.fileSystem))
                .Split()
                .Where(file => string.Equals(Path.GetExtension(file), ".keep", FileSystemHelpers.PathComparison))
                .Count()
                .ShouldEqual(1, "Incorrect number of .keep files in pack directory");

            this.Enlistment.Prefetch("--commits");
            this.PostFetchStepShouldComplete();
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(12)]
        public void PrefetchFilesFromFileListFile()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            try
            {
                File.WriteAllLines(
                    tempFilePath,
                    new[]
                    {
                        Path.Combine("GVFS", "GVFS", "Program.cs"),
                        Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")
                    });

                this.ExpectBlobCount(this.Enlistment.Prefetch($"--files-list \"{tempFilePath}\""), 2);
            }
            finally
            {
                File.Delete(tempFilePath);
            }
        }

        [TestCase, Order(13)]
        [SkipInCI("Flaky: stdin prefetch blob count varies in CI")]
        public void PrefetchFilesFromFileListStdIn()
        {
            // on case-insensitive filesystems, test case-blind matching
            // using "App.config" instead of "app.config"
            string input = string.Join(
                Environment.NewLine,
                new[]
                {
                    Path.Combine("GVFS", "GVFS", "packages.config"),
                    Path.Combine("GVFS", "GVFS.FunctionalTests", FileSystemHelpers.CaseSensitiveFileSystem ? "app.config" : "App.config")
                });

            this.ExpectBlobCount(this.Enlistment.Prefetch("--stdin-files-list", standardInput: input), 2);
        }

        [TestCase, Order(14)]
        [SkipInCI("Flaky: stdin prefetch blob count varies in CI")]
        public void PrefetchFolderListFromStdin()
        {
            string input = string.Join(Environment.NewLine, PrefetchFolderList);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--stdin-folders-list", standardInput: input), 279);
        }

        public void PrefetchPathsWithLsTreeTypeInPath()
        {
            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + LsTreeTypeInPathBranchName);

            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 496);
        }

        private void ExpectBlobCount(string output, int expectedCount)
        {
            output.ShouldContain("Matched blobs:    " + expectedCount);
        }

        private void PostFetchStepShouldComplete()
        {
            string objectDir = this.Enlistment.GetObjectRoot(this.fileSystem);
            string objectCacheLock = Path.Combine(objectDir, "git-maintenance-step.lock");

            // Wait first, to hopefully ensure the background thread has
            // started before we check for the lock file.
            do
            {
                Thread.Sleep(500);
            }
            while (this.fileSystem.FileExists(objectCacheLock));

            // A commit graph is not always generated, but if it is, then we want to ensure it is in a good state
            if (this.fileSystem.FileExists(Path.Combine(objectDir, "info", "commit-graphs", "commit-graph-chain")))
            {
                ProcessResult graphResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "commit-graph verify --shallow --object-dir=\"" + objectDir + "\"");
                graphResult.ExitCode.ShouldEqual(0);
            }
        }
    }
}
