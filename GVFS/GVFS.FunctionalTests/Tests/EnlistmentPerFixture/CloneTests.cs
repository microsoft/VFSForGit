using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class CloneTests : TestsWithEnlistmentPerFixture
    {
        private const int GVFSGenericError = 3;

        [TestCase]
        public void CloneInsideMountedEnlistment()
        {
            this.SubfolderCloneShouldFail();
        }

        [TestCase]
        public void CloneInsideUnmountedEnlistment()
        {
            this.Enlistment.UnmountGVFS();
            this.SubfolderCloneShouldFail();
            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void CloneWithLocalCachePathWithinSrc()
        {
            string newEnlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();

            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = $"clone {Properties.Settings.Default.RepoToClone} {newEnlistmentRoot} --local-cache-path {Path.Combine(newEnlistmentRoot, "src", ".gvfsCache")}";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = Path.GetDirectoryName(this.Enlistment.EnlistmentRoot);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(GVFSGenericError);
            result.Output.ShouldContain("'--local-cache-path' cannot be inside the src folder");
        }

        [TestCase]
        public void CloneToPathWithSpaces()
        {
            GVFSFunctionalTestEnlistment enlistment = GVFSFunctionalTestEnlistment.CloneAndMountEnlistmentWithSpacesInPath(GVFSTestConfig.PathToGVFS);
            enlistment.UnmountAndDeleteAll();
        }

        [TestCase]
        public void CloneCreatesCorrectFilesInRoot()
        {
            GVFSFunctionalTestEnlistment enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(GVFSTestConfig.PathToGVFS);
            try
            {
                string[] files = Directory.GetFiles(enlistment.EnlistmentRoot);
                files.Length.ShouldEqual(1);
                files.ShouldContain(x => Path.GetFileName(x).Equals("git.cmd", StringComparison.Ordinal));
                string[] directories = Directory.GetDirectories(enlistment.EnlistmentRoot);
                directories.Length.ShouldEqual(2);
                directories.ShouldContain(x => Path.GetFileName(x).Equals(GVFSTestConfig.DotGVFSRoot, StringComparison.Ordinal));
                directories.ShouldContain(x => Path.GetFileName(x).Equals("src", StringComparison.Ordinal));
            }
            finally
            {
                enlistment.UnmountAndDeleteAll();
            }
        }

        private void SubfolderCloneShouldFail()
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = "clone " + GVFSTestConfig.RepoToClone + " src\\gvfs\\test1";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(GVFSGenericError);
            result.Output.ShouldContain("You can't clone inside an existing GVFS repo");
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class CloneNetworkTests
    {
        private string seedRoot;
        private string localCacheRoot;

        [OneTimeSetUp]
        public void CreateObjectCache()
        {
            this.seedRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(this.seedRoot);
            this.localCacheRoot = Path.Combine(this.seedRoot, "cache");
            ProcessStartInfo process = this.CreateCloneProcess(
                this.seedRoot, GVFSTestConfig.RepoToClone, Path.Combine(this.seedRoot, "seed"),
                "--branch", Properties.Settings.Default.Commitish,
                "--local-cache-path", this.localCacheRoot, "--no-mount", "--no-prefetch");
            ProcessResult result = ProcessHelper.Run(process, timeoutMs: 75000);
            result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
        }

        [OneTimeTearDown]
        public void DeleteObjectCache()
        {
            RepositoryHelpers.DeleteTestDirectory(this.seedRoot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrefetchBannerIsLimitedToCloneLaunchedProcesses(bool fromClone)
        {
            string enlistmentRoot = Path.Combine(this.seedRoot, "seed");
            ProcessResult result = ProcessHelper.Run(
                GVFSTestConfig.PathToGVFS,
                $"prefetch \"{enlistmentRoot}\" --commits --hydrate" + (fromClone ? " --from-clone" : string.Empty),
                this.seedRoot,
                timeoutMs: 30000);

            result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
            result.Output.ShouldContain("You can only specify --hydrate with --files or --folders");
            if (fromClone)
            {
                result.Output.ShouldContain("VFS for Git: history prefetch");
                result.Output.ShouldContain("Repository: " + enlistmentRoot);
                result.Output.ShouldContain("Prefetch client process ID: ");
                result.Output.ShouldContain("After mount succeeds, you can use the repository while prefetch runs.");
                result.Output.ShouldContain("gvfs prefetch --commits");
                string logPath = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Single(line => line.StartsWith("Log: ", StringComparison.Ordinal))
                    .Substring("Log: ".Length);
                File.Exists(logPath).ShouldEqual(true, "The banner must identify its actual log file.");
                Directory.GetFiles(Path.Combine(enlistmentRoot, ".gvfs", "logs"), "gvfs_prefetch_*.log")
                    .ShouldContain(path => path.Equals(logPath, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                result.Output.Contains("VFS for Git: history prefetch").ShouldEqual(false);
                result.Output.Contains("Prefetch client process ID: ").ShouldEqual(false);
            }
        }

        [TestCase]
        public async Task CloneReportsBackgroundPrefetchProcessId()
        {
            string root = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, "clone with spaces");
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeCloneRequests(listener, cancellation.Token, path =>
            {
                path.ShouldContain("/gvfs/prefetch");
                return ("application/x-gvfs-timestamped-packfiles-indexes", null);
            });
            Process prefetch = null;
            Process clone = null;
            try
            {
                ProcessStartInfo process = this.CreateCloneProcess(
                    root, GVFSTestConfig.RepoToClone, destination,
                    "--branch", Properties.Settings.Default.Commitish,
                    "--local-cache-path", this.localCacheRoot,
                    "--cache-server-url", endpoint, "--no-mount");
                string config = process.Environment["GIT_CONFIG_GLOBAL"];
                foreach (string setting in new[] { "gvfs.trust-pack-indexes false", "gvfs.read-timeout-seconds 30", "gvfs.max-retries 0" })
                {
                    GitProcess.InvokeProcess(root, $"config --file \"{config}\" {setting}").ExitCode.ShouldEqual(0);
                }

                clone = Process.Start(process);
                Task<string> errors = clone.StandardError.ReadToEndAsync(cancellation.Token);
                StringBuilder output = new StringBuilder();
                while (prefetch == null)
                {
                    string line = await clone.StandardOutput.ReadLineAsync(cancellation.Token);
                    if (line == null)
                    {
                        Assert.Fail("Clone did not report a prefetch client PID.\r\n" + output + await errors);
                    }

                    output.AppendLine(line);
                    Match match = Regex.Match(line, @"minimized window \(client PID (\d+)\)");
                    if (match.Success)
                    {
                        prefetch = Process.GetProcessById(int.Parse(match.Groups[1].Value));
                    }
                }

                output.ToString().ShouldContain(destination);
                clone.WaitForExit(10000).ShouldEqual(true, "Clone must not wait for background prefetch.");
                clone.ExitCode.ShouldEqual(0, output.ToString());
                prefetch.HasExited.ShouldEqual(false, "The reported PID must identify the active prefetch process.");
                prefetch.MainModule.FileName.Equals(GVFSTestConfig.PathToGVFS, StringComparison.OrdinalIgnoreCase)
                    .ShouldEqual(true, "The reported PID must identify the GVFS client.");
                string logPath = null;
                SpinWait.SpinUntil(
                    () =>
                    {
                        logPath = Directory.GetFiles(
                            Path.Combine(destination, ".gvfs", "logs"), "gvfs_prefetch_*.log").SingleOrDefault();
                        return logPath != null &&
                            GVFSHelpers.ReadAllTextFromWriteLockedFile(logPath).Contains("\"Commits\":true");
                    },
                    TimeSpan.FromSeconds(30)).ShouldEqual(true, "The prefetch client must write commit-prefetch metadata.");
                string log = GVFSHelpers.ReadAllTextFromWriteLockedFile(logPath);
                log.ShouldContain("\"Commits\":true");
                log.ShouldContain("\"EnlistmentRoot\":" + System.Text.Json.JsonSerializer.Serialize(destination));
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await ObserveCloneServer(server, cancellation.Token);
                }
                finally
                {
                    if (prefetch != null)
                    {
                        using (prefetch)
                        {
                            if (!prefetch.HasExited)
                            {
                                prefetch.Kill(entireProcessTree: true);
                                prefetch.WaitForExit(5000).ShouldEqual(true);
                            }
                        }
                    }

                    try
                    {
                        if (clone != null)
                        {
                            using (clone)
                            {
                                if (!clone.HasExited)
                                {
                                    clone.Kill(entireProcessTree: true);
                                    clone.WaitForExit(5000).ShouldEqual(true);
                                }
                            }
                        }
                    }
                    finally
                    {
                        RepositoryHelpers.DeleteTestDirectory(root);
                    }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CloneRetriesStalledInfoRefsWithinTheRequestBudget(bool recover)
        {
            string root = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(root);
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            int infoRefsRequests = 0;
            Task server = ServeCloneRequests(listener, cancellation.Token, path =>
            {
                if (path.StartsWith("/gvfs/config", StringComparison.Ordinal))
                {
                    return ("application/json", Encoding.UTF8.GetBytes("{}"));
                }

                path.ShouldContain("/info/refs");
                ++infoRefsRequests;
                string reference = "1111111111111111111111111111111111111111 refs/heads/other\n";
                return ("application/x-git-upload-pack-advertisement",
                    recover && infoRefsRequests > 1
                        ? Encoding.ASCII.GetBytes((reference.Length + 4).ToString("x4") + reference)
                        : null);
            });
            try
            {
                ProcessStartInfo process = this.CreateCloneProcess(
                    root, endpoint, Path.Combine(root, "clone"), "--branch", "missing", "--no-mount", "--no-prefetch",
                    "--cache-server-url", "None");
                ProcessResult result = await Task.Run(() => ProcessHelper.Run(process, timeoutMs: 30000));
                result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
                string log = File.ReadAllText(Directory.GetFiles(
                    Path.Combine(root, "clone", ".gvfs", "logs"), "gvfs_clone_*.log").Single());
                log.ShouldContain("NetworkBodyReadTimeout");
                log.ShouldContain("\"ReadTimeoutMs\":1000");
                log.ShouldContain("\"AttemptNumber\":1");
                log.ShouldContain("\"WillRetry\":true");
                infoRefsRequests.ShouldEqual(2);
                if (recover)
                {
                    result.Output.ShouldContain("Remote branch missing not found");
                }
                else
                {
                    log.ShouldContain("\"AttemptNumber\":2");
                    log.ShouldContain("\"WillRetry\":false");
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await ObserveCloneServer(server, cancellation.Token);
                }
                finally
                {
                    RepositoryHelpers.DeleteTestDirectory(root);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CloneDistinguishesPrefetchFailureFromMountFailure(bool failMount)
        {
            string root = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, "clone");
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            int prefetchRequests = 0;
            Task server = ServeCloneRequests(listener, cancellation.Token, path =>
            {
                path.ShouldContain("/gvfs/prefetch");
                ++prefetchRequests;
                if (failMount && prefetchRequests == 1)
                {
                    GVFSHelpers.SaveDiskLayoutVersion(Path.Combine(destination, ".gvfs"), "999", "0");
                }

                return ("application/x-gvfs-timestamped-packfiles-indexes", null);
            });
            try
            {
                ProcessStartInfo process = this.CreateCloneProcess(
                    root, GVFSTestConfig.RepoToClone, destination,
                    "--branch", Properties.Settings.Default.Commitish,
                    "--local-cache-path", this.localCacheRoot,
                    "--cache-server-url", endpoint);
                if (!failMount)
                {
                    process.ArgumentList.Add("--no-mount");
                }

                ProcessResult result = await Task.Run(() => ProcessHelper.Run(process, timeoutMs: 75000));
                result.ExitCode.ShouldEqual(failMount ? 3 : 13, result.Output + result.Errors);
                result.Output.ShouldContain("Clone completed, but prefetch failed");
                result.Output.ShouldContain("Run 'gvfs prefetch --commits'");
                if (failMount)
                {
                    result.Output.ShouldContain("disk layout");
                }

                (prefetchRequests > 0).ShouldEqual(true);
                ProcessResult head = GitProcess.InvokeProcess(Path.Combine(destination, "src"), "rev-parse --verify HEAD");
                head.ExitCode.ShouldEqual(0, head.Errors);
                head.Output.Trim().Length.ShouldEqual(40);
                if (!failMount)
                {
                    string prefetchLog = File.ReadAllText(Directory.GetFiles(
                        Path.Combine(destination, ".gvfs", "logs"), "gvfs_prefetch_*.log").Single());
                    prefetchLog.ShouldContain("NetworkBodyReadTimeout");
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await ObserveCloneServer(server, cancellation.Token);
                }
                finally
                {
                    new GVFSProcess(GVFSTestConfig.PathToGVFS, destination, this.localCacheRoot).Unmount();
                    RepositoryHelpers.DeleteTestDirectory(root);
                }
            }
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        public async Task CloneAnnouncesOnlyInitialBackgroundPrefetch(bool cachedPack, bool noPrefetch, bool trustPackIndexes)
        {
            string root = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, "clone");
            string packRoot = Path.Combine(Directory.GetDirectories(
                this.localCacheRoot, "gitObjects", SearchOption.AllDirectories).Single(), "pack");
            string cachedPackPath = null;
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            int prefetchRequests = 0;
            Task server = ServeCloneRequests(listener, cancellation.Token, path =>
            {
                path.ShouldContain("/gvfs/prefetch");
                Interlocked.Increment(ref prefetchRequests);
                return ("application/x-gvfs-timestamped-packfiles-indexes", new byte[]
                {
                    (byte)'G', (byte)'P', (byte)'R', (byte)'E', (byte)' ', 1, 0, 0
                });
            });
            try
            {
                if (cachedPack)
                {
                    string source = Path.Combine(root, "pack-source");
                    GitProcess.InvokeProcess(root, $"init --object-format=sha1 \"{source}\"").ExitCode.ShouldEqual(0);
                    string prefix = Path.Combine(packRoot, "prefetch-1000");
                    ProcessResult pack = GitProcess.InvokeProcess(source, $"pack-objects --all \"{prefix}\"");
                    pack.ExitCode.ShouldEqual(0, pack.Errors);
                    cachedPackPath = prefix + "-" + pack.Output.Trim() + ".pack";
                    File.Exists(cachedPackPath).ShouldBeTrue();
                    File.Exists(Path.ChangeExtension(cachedPackPath, ".idx")).ShouldBeTrue();
                }

                ProcessStartInfo process = this.CreateCloneProcess(
                    root, GVFSTestConfig.RepoToClone, destination,
                    "--branch", Properties.Settings.Default.Commitish,
                    "--local-cache-path", this.localCacheRoot,
                    "--cache-server-url", endpoint, "--no-mount");
                GitProcess.InvokeProcess(
                    root, $"config --file \"{process.Environment["GIT_CONFIG_GLOBAL"]}\" gvfs.trust-pack-indexes {trustPackIndexes.ToString().ToLowerInvariant()}")
                    .ExitCode.ShouldEqual(0);
                if (noPrefetch)
                {
                    process.ArgumentList.Add("--no-prefetch");
                }

                ProcessResult result = await Task.Run(() => ProcessHelper.Run(process, timeoutMs: 75000));
                result.ExitCode.ShouldEqual(0, result.Output + result.Errors);
                result.Output.Contains("Prefetch of commit graph has been started")
                    .ShouldEqual(!cachedPack && !noPrefetch && !trustPackIndexes, result.Output);

                string logsRoot = Path.Combine(destination, ".gvfs", "logs");
                if (noPrefetch)
                {
                    Volatile.Read(ref prefetchRequests).ShouldEqual(0);
                    Directory.GetFiles(logsRoot, "gvfs_prefetch_*.log").Length.ShouldEqual(0);
                }
                else
                {
                    Stopwatch wait = Stopwatch.StartNew();
                    string[] logs;
                    string prefetchLog = string.Empty;
                    do
                    {
                        logs = Directory.GetFiles(logsRoot, "gvfs_prefetch_*.log");
                        if (logs.Length == 1)
                        {
                            using FileStream stream = new FileStream(
                                logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using StreamReader reader = new StreamReader(stream);
                            prefetchLog = reader.ReadToEnd();
                            if (prefetchLog.Contains(" Prefetch (Stop)"))
                            {
                                break;
                            }
                        }

                        await Task.Delay(50, cancellation.Token);
                    }
                    while (wait.Elapsed < TimeSpan.FromSeconds(30));

                    logs.Length.ShouldEqual(1, "Prefetch must still run when the announcement is suppressed.");
                    prefetchLog.ShouldContain(" Prefetch (Stop)");
                    (Volatile.Read(ref prefetchRequests) > 0).ShouldBeTrue();
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await ObserveCloneServer(server, cancellation.Token);
                }
                finally
                {
                    if (cachedPackPath != null)
                    {
                        foreach (string file in new[]
                        {
                            Path.ChangeExtension(cachedPackPath, ".keep"),
                            Path.ChangeExtension(cachedPackPath, ".idx"),
                            cachedPackPath,
                        })
                        {
                            if (File.Exists(file))
                            {
                                File.SetAttributes(file, FileAttributes.Normal);
                                File.Delete(file);
                            }
                        }
                    }

                    RepositoryHelpers.DeleteTestDirectory(root);
                }
            }
        }

        private ProcessStartInfo CreateCloneProcess(string root, string repository, string destination, params string[] options)
        {
            string config = Path.Combine(root, ".gitconfig");
            string originalGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
            GitProcess.InvokeProcess(root, $"config --file \"{config}\" include.path \"{originalGlobal}\"")
                .ExitCode.ShouldEqual(0);
            foreach (string setting in new[]
            {
                "gvfs.read-timeout-seconds 1",
                "gvfs.max-retries 1",
                "gvfs.trust-pack-indexes true",
                "gvfs.prefetch-offload false",
            })
            {
                GitProcess.InvokeProcess(root, $"config --file \"{config}\" {setting}").ExitCode.ShouldEqual(0);
            }

            ProcessStartInfo process = new ProcessStartInfo(GVFSTestConfig.PathToGVFS)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
            };
            process.Environment["GIT_CONFIG_GLOBAL"] = config;
            process.Environment["HOME"] = root;
            process.ArgumentList.Add("clone");
            process.ArgumentList.Add(repository);
            process.ArgumentList.Add(destination);
            foreach (string option in options)
            {
                process.ArgumentList.Add(option);
            }

            return process;
        }

        private static async Task ServeCloneRequests(
            TcpListener listener,
            CancellationToken cancellationToken,
            Func<string, (string ContentType, byte[] Body)> respond)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream stream = client.GetStream();
                string request;
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                {
                    request = await reader.ReadLineAsync(cancellationToken);
                    string line;
                    do
                    {
                        line = await reader.ReadLineAsync(cancellationToken);
                    }
                    while (!string.IsNullOrEmpty(line));
                }

                (string contentType, byte[] body) = respond(request.Split(' ')[1]);
                bool stall = body == null;
                byte[] data = body ?? new byte[] { (byte)'G' };
                byte[] headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: {contentType}\r\nContent-Length: {(stall ? 1024 : data.Length)}\r\n\r\n");
                await stream.WriteAsync(headers.AsMemory(), cancellationToken);
                await stream.WriteAsync(data.AsMemory(), cancellationToken);
                if (stall)
                {
                    (await stream.ReadAsync(new byte[1].AsMemory(), cancellationToken)).ShouldEqual(0);
                }
            }
        }

        private static async Task ObserveCloneServer(Task server, CancellationToken cancellationToken)
        {
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
