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
                result.ExitCode.ShouldEqual(failMount ? 3 : 12, result.Output + result.Errors);
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

        private ProcessStartInfo CreateCloneProcess(string root, string repository, string destination, params string[] options)
        {
            string config = Path.Combine(root, "global.config");
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
