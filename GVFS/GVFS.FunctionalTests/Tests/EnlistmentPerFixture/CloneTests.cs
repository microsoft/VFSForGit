using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
                    using MemoryStream packInput = new MemoryStream();
                    ProcessResult pack = GitProcess.InvokeProcess(source, $"pack-objects --all \"{prefix}\"", inputStream: packInput);
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

        internal static async Task ServeCloneRequests(
            TcpListener listener,
            CancellationToken cancellationToken,
            Func<string, (string ContentType, byte[] Body)> respond,
            Action<string> observeBody = null)
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
                    int contentLength = 0;
                    do
                    {
                        line = await reader.ReadLineAsync(cancellationToken);
                        if (line != null && line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line.Substring("Content-Length:".Length));
                        }
                    }
                    while (!string.IsNullOrEmpty(line));

                    char[] requestBody = new char[contentLength];
                    if (contentLength > 0)
                    {
                        (await reader.ReadBlockAsync(requestBody.AsMemory(), cancellationToken)).ShouldEqual(contentLength);
                    }
                    observeBody?.Invoke(new string(requestBody));
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

        internal static async Task ObserveCloneServer(Task server, CancellationToken cancellationToken)
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

    [TestFixture]
    [NonParallelizable]
    public class CloneCachedTreesTests
    {
        [TestCase]
        public Task CloneRecoversMissingNestedTree()
        {
            return this.CloneReusesCachedTreesAndRecoversMissingTrees(true, "nested", 1, true);
        }

        [TestCase("empty")]
        [TestCase("zeroed")]
        [TestCase("truncated")]
        public Task CloneRecoversCorruptRootTree(string corruption)
        {
            return this.CloneReusesCachedTreesAndRecoversMissingTrees(true, "root-" + corruption, 1, true);
        }

        [TestCase(true, "complete", 0, true)]
        [TestCase(false, "complete", 1, true)]
        [TestCase(true, "commit", 1, true)]
        [TestCase(true, "root", 1, true)]
        [TestCase(true, "download-fails", 1, false)]
        [TestCase(true, "download-incomplete", 1, false)]
        [TestCase(false, "packed", 0, true)]
        [TestCase(true, "packed-loose", 0, true)]
        [TestCase(false, "packed-loose", 1, true)]
        [TestCase(true, "global-override", 1, true)]
        [TestCase(false, "global-override", 0, true)]
        [TestCase(true, "process-override", 1, true)]
        [TestCase(false, "process-override", 0, true)]
        [TestCase(false, "root-empty", 1, true)]
        [TestCase(false, "root-zeroed", 1, true)]
        [TestCase(false, "root-truncated", 1, true)]
        [TestCase(true, "root-download-fails", 1, false)]
        [TestCase(true, "root-download-incomplete", 1, false)]
        [TestCase(true, "root-nested-incomplete", 1, false)]
        public async Task CloneReusesCachedTreesAndRecoversMissingTrees(
            bool optimisticCheckout, string cacheState, int expectedDownloads, bool expectSuccess)
        {
            string root = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source");
            string cache = Path.Combine(root, "cache");
            string destination = Path.Combine(root, "clone");
            string globalConfig = Path.Combine(root, ".gitconfig");
            File.WriteAllText(
                globalConfig,
                $"[gvfs]\noptimistic-clone-checkout = {optimisticCheckout.ToString().ToLowerInvariant()}\nmax-retries = 0\n");
            Dictionary<string, string> environment = new Dictionary<string, string>
                {
                    { "GIT_CONFIG_GLOBAL", globalConfig },
                    { "GIT_CONFIG_NOSYSTEM", "1" },
                    { "HOME", root },
                };
            bool effectiveOptimisticCheckout = optimisticCheckout;
            if (cacheState == "global-override" || cacheState == "process-override")
            {
                effectiveOptimisticCheckout = !optimisticCheckout;
                string overrideValue = effectiveOptimisticCheckout.ToString().ToLowerInvariant();
                if (cacheState == "global-override")
                {
                    string selectedConfig = Path.Combine(root, "selected.config");
                    File.WriteAllText(selectedConfig, $"[gvfs]\noptimistic-clone-checkout = {overrideValue}\nmax-retries = 0\n");
                    environment["GIT_CONFIG_GLOBAL"] = selectedConfig;
                }
                else
                {
                    environment["GIT_CONFIG_COUNT"] = "1";
                    environment["GIT_CONFIG_KEY_0"] = "gvfs.optimistic-clone-checkout";
                    environment["GIT_CONFIG_VALUE_0"] = overrideValue;
                }
            }
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            Task server = null;
            try
            {
                GitProcess.InvokeProcess(root, $"init \"{source}\"", environment).ExitCode.ShouldEqual(0);
                File.WriteAllText(Path.Combine(source, ".gitattributes"), "* -text\n");
                Directory.CreateDirectory(Path.Combine(source, "sub", "folder"));
                File.WriteAllText(Path.Combine(source, "sub", "folder", "file.txt"), "test\n");
                GitProcess.InvokeProcess(source, "-c core.hooksPath=NUL add .", environment).ExitCode.ShouldEqual(0);
                GitProcess.InvokeProcess(
                    source,
                    "-c core.hooksPath=NUL -c user.name=Test -c user.email=test@example.com commit -m test",
                    environment).ExitCode.ShouldEqual(0);
                string commit = GitProcess.Invoke(source, "rev-parse HEAD").Trim();
                string rootTree = GitProcess.Invoke(source, "rev-parse HEAD^{tree}").Trim();
                string nestedTree = GitProcess.Invoke(source, "rev-parse HEAD:sub/folder").Trim();

                listener.Start();
                string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
                const string CacheKey = "cached-trees";
                string objects = Path.Combine(cache, CacheKey, "gitObjects");
                Directory.CreateDirectory(objects);
                File.WriteAllText(
                    Path.Combine(cache, "mapping.dat"),
                    "A {\"Key\":\"GVFS_LocalCache_MappingVersion\",\"Value\":\"1\"}\r\n" +
                    $"A {{\"Key\":\"{endpoint}\",\"Value\":\"{CacheKey}\"}}\r\n");
                foreach (string directory in Directory.GetDirectories(Path.Combine(source, ".git", "objects")))
                {
                    if (Path.GetFileName(directory).Length != 2)
                    {
                        continue;
                    }

                    string target = Path.Combine(objects, Path.GetFileName(directory));
                    Directory.CreateDirectory(target);
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
                    }
                }

                bool corruptRootTree = cacheState.StartsWith("root-", StringComparison.Ordinal);
                bool missingNestedTree = cacheState == "nested" || cacheState == "download-fails" ||
                    cacheState == "download-incomplete" || cacheState == "root-nested-incomplete";
                string missingObject = cacheState == "commit" ? commit :
                    cacheState == "root" ? rootTree :
                    missingNestedTree ? nestedTree : null;
                if (missingObject != null)
                {
                    string path = Path.Combine(objects, missingObject.Substring(0, 2), missingObject.Substring(2));
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                if (corruptRootTree)
                {
                    string rootTreePath = Path.Combine(objects, rootTree.Substring(0, 2), rootTree.Substring(2));
                    byte[] rootTreeBytes = File.ReadAllBytes(rootTreePath);
                    byte[] corruptBytes = cacheState == "root-zeroed" ? new byte[rootTreeBytes.Length] :
                        cacheState == "root-truncated" ? rootTreeBytes.Take(rootTreeBytes.Length / 2).ToArray() :
                        Array.Empty<byte>();
                    File.SetAttributes(rootTreePath, FileAttributes.Normal);
                    File.WriteAllBytes(rootTreePath, corruptBytes);
                }

                string[] commitAndTrees = GitProcess.Invoke(
                    source, "rev-list --objects --no-object-names --filter=blob:none HEAD")
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (cacheState == "packed" || cacheState == "packed-loose")
                {
                    string packDirectory = Path.Combine(objects, "pack");
                    Directory.CreateDirectory(packDirectory);
                    using MemoryStream cacheInput = new MemoryStream(Encoding.ASCII.GetBytes(string.Join("\n", commitAndTrees) + "\n"));
                    ProcessResult cachePack = GitProcess.InvokeProcess(
                        source, $"pack-objects \"{Path.Combine(packDirectory, "pack")}\"", environment, cacheInput);
                    cachePack.ExitCode.ShouldEqual(0, cachePack.Errors);
                    if (cacheState == "packed")
                    {
                        string looseCommit = Path.Combine(objects, commit.Substring(0, 2), commit.Substring(2));
                        File.SetAttributes(looseCommit, FileAttributes.Normal);
                        File.Delete(looseCommit);
                    }
                }

                string omittedTree = cacheState == "root-download-incomplete" ? rootTree :
                    cacheState == "download-incomplete" || cacheState == "root-nested-incomplete" ? nestedTree : null;
                IEnumerable<string> servedObjects = commitAndTrees.Where(objectId => objectId != omittedTree);
                using MemoryStream input = new MemoryStream(Encoding.ASCII.GetBytes(string.Join("\n", servedObjects) + "\n"));
                string packPrefix = Path.Combine(root, "server-pack");
                ProcessResult packed = GitProcess.InvokeProcess(source, $"pack-objects \"{packPrefix}\"", environment, input);
                packed.ExitCode.ShouldEqual(0, packed.Errors);
                byte[] pack = cacheState == "download-fails" || cacheState == "root-download-fails"
                    ? Encoding.ASCII.GetBytes("invalid pack")
                    : File.ReadAllBytes(packPrefix + "-" + packed.Output.Trim() + ".pack");
                int downloads = 0;
                List<string> requestedObjects = new List<string>();
                server = CloneNetworkTests.ServeCloneRequests(listener, cancellation.Token, path =>
                {
                    if (path.StartsWith("/gvfs/config", StringComparison.Ordinal))
                    {
                        return ("application/json", Encoding.UTF8.GetBytes("{}"));
                    }

                    if (path.StartsWith("/info/refs", StringComparison.Ordinal))
                    {
                        string reference = commit + " refs/heads/test-branch\n";
                        return ("application/x-git-upload-pack-advertisement",
                            Encoding.ASCII.GetBytes((reference.Length + 4).ToString("x4") + reference));
                    }

                    path.ShouldEqual("/gvfs/objects");
                    ++downloads;
                    return ("application/x-git-packfile", pack);
                }, body =>
                {
                    if (body.Length > 0)
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        document.RootElement.GetProperty("commitDepth").GetInt32().ShouldEqual(1);
                        foreach (JsonElement objectId in document.RootElement.GetProperty("objectIds").EnumerateArray())
                        {
                            requestedObjects.Add(objectId.GetString());
                        }
                    }
                });
                ProcessStartInfo process = new ProcessStartInfo(GVFSTestConfig.PathToGVFS)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = root,
                };
                foreach (KeyValuePair<string, string> variable in environment)
                {
                    process.Environment[variable.Key] = variable.Value;
                }

                foreach (string argument in new[]
                {
                        "clone", endpoint, destination, "--branch", "test-branch",
                        "--cache-server-url", "None", "--local-cache-path", cache, "--no-mount", "--no-prefetch",
                    })
                {
                    process.ArgumentList.Add(argument);
                }

                ProcessResult result;
                try
                {
                    result = await Task.Run(() => ProcessHelper.Run(process, timeoutMs: 90000));
                }
                catch (TimeoutException)
                {
                    string logs = Path.Combine(destination, ".gvfs", "logs");
                    if (Directory.Exists(logs))
                    {
                        foreach (string file in Directory.GetFiles(logs, "gvfs_clone_*.log"))
                        {
                            TestContext.WriteLine(File.ReadAllText(file));
                        }
                    }

                    throw;
                }

                result.ExitCode.ShouldEqual(expectSuccess ? 0 : 3, result.Output + result.Errors);
                downloads.ShouldEqual(expectedDownloads);
                requestedObjects.Count.ShouldEqual(expectedDownloads);
                foreach (string requestedObject in requestedObjects)
                {
                    requestedObject.ShouldEqual(commit);
                }
                string log = File.ReadAllText(Directory.GetFiles(
                    Path.Combine(destination, ".gvfs", "logs"), "gvfs_clone_*.log").Single());
                bool reusedCommit = effectiveOptimisticCheckout && cacheState != "commit" && cacheState != "root";
                log.Contains("Clone_UseCachedCommit").ShouldEqual(reusedCommit, log);
                if (reusedCommit && missingNestedTree && !corruptRootTree)
                {
                    log.ShouldContain("Download the commit pack and retry checkout.");
                    log.ShouldContain($"fatal: unable to read tree ({nestedTree})");
                }

                if (reusedCommit && corruptRootTree)
                {
                    log.ShouldContain("Download the commit pack and retry root attributes.");
                    log.ShouldContain("Error returned from ls-tree");
                    log.Contains("Clone checkout could not read a tree.").ShouldBeFalse(log);
                }

                if (!expectSuccess)
                {
                    (result.Output + result.Errors).ShouldContain(cacheState == "download-fails" || cacheState == "root-download-fails"
                        ? "Could not download commit"
                        : cacheState == "root-download-incomplete" ? "Error returned from ls-tree"
                        : "Could not complete checkout of branch: test-branch");
                }

                if (expectSuccess)
                {
                    string repo = Path.Combine(destination, "src");
                    GitProcess.Invoke(repo, "-c core.hooksPath=NUL rev-parse HEAD").Trim().ShouldEqual(commit);
                    ProcessResult index = GitProcess.InvokeProcess(
                        repo, "-c core.hooksPath=NUL -c core.virtualFileSystem= -c core.gvfs=0 write-tree");
                    index.ExitCode.ShouldEqual(0, index.Errors);
                    index.Output.Trim().ShouldEqual(rootTree);
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    if (server != null)
                    {
                        await CloneNetworkTests.ObserveCloneServer(server, cancellation.Token);
                    }
                }
                finally
                {
                    RepositoryHelpers.DeleteTestDirectory(root);
                }
            }
        }
    }
}
