using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static GVFS.UnitTests.Http.HttpResponseStreamTestSupport;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class HttpResponseStreamTests
    {
        [SetUp]
        public void SetUp()
        {
            RetryCircuitBreaker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RetryCircuitBreaker.Reset();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task RealHttpBodyReadTimesOutAfterHeadersAndPartialData()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            using HttpClient client = new HttpClient();
            listener.Start();
            Task server = ServeStalledResponse(listener, cancellation.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (HttpResponseMessage response = await client.GetAsync(
                    $"http://127.0.0.1:{port}/body",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)))
                using (HttpResponseStream stream = CreateStream(await response.Content.ReadAsStreamAsync(cancellation.Token), tracer))
                {
                    stream.ReadByte().ShouldEqual((int)'x');
                    Assert.ThrowsAsync<IOException>(
                        async () => await stream.ReadAsync(new byte[8].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                    stream.ReadFailed.ShouldEqual(true);
                    LastBodyEvent(tracer, "TimedOut").Name.ShouldEqual("NetworkBodyReadTimeout");
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase(HttpStatusCode.OK)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.NotFound)]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task RequestorAppliesConfiguredReadTimeoutToSuccessAndErrorBodies(HttpStatusCode status)
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task server = ServeStalledResponse(listener, cancellation.Token, status);
            try
            {
                using LoopbackHttpRequestor requestor = new LoopbackHttpRequestor(tracer);
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Uri uri = new Uri($"http://127.0.0.1:{port}/body?query-marker=not-for-telemetry");
                if (status == HttpStatusCode.OK)
                {
                    using (GitEndPointResponseData response = requestor.Get(uri))
                    {
                        response.Stream.ReadByte().ShouldEqual((int)'x');
                        Assert.Throws<IOException>(() => response.Stream.ReadByte());
                        response.StreamReadFailed.ShouldEqual(true);
                        response.ResponseId.ShouldEqual((string)LastBodyEvent(tracer, "TimedOut").Metadata["ResponseId"]);
                    }
                }
                else
                {
                    using (GitEndPointResponseData response = requestor.Get(uri))
                    {
                        response.StatusCode.ShouldEqual(status);
                        ((GitObjectsHttpException)response.Error).StatusCode.ShouldEqual(status);
                        response.Error.Message.ShouldContain("response body");
                        response.Error.Message.ShouldContain("No response body data arrived");
                        response.ShouldRetry.ShouldEqual(status == HttpStatusCode.ServiceUnavailable);
                    }
                }

                EventMetadata timeout = LastBodyEvent(tracer, "TimedOut").Metadata;
                timeout["RequestId"].ShouldEqual(42L);
                timeout["StatusCode"].ShouldEqual(status);
                timeout["CacheName"].ShouldEqual("test-cache");
                timeout["RequestAuthority"].ShouldEqual($"127.0.0.1:{port}");
                timeout["BytesRead"].ShouldEqual(1L);
                timeout["ReadTimeoutMs"].ShouldEqual(300D);
                timeout.ContainsKey("ContentType").ShouldEqual(false);
                GVFSJsonOptions.Serialize(timeout).Contains("query-marker", StringComparison.Ordinal).ShouldEqual(false);
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task StalledErrorBodyRetriesWithTheStatusAndReleasesItsConnectionPermit()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeRetriedError(listener, cancellation.Token);
            try
            {
                using GitObjectsHttpRequestor requestor = new GitObjectsHttpRequestor(
                    tracer, new MockGVFSEnlistment(), new CacheServerInfo(endpoint, "test"),
                    new RetryConfig(1, TimeSpan.FromSeconds(5)) { ReadTimeout = TimeSpan.FromMilliseconds(100) });
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                    requestor.TrySendProtocolRequest(
                        123,
                        (attempt, response) =>
                        {
                            response.Stream.ReadByte().ShouldEqual(-1);
                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                                new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                        },
                        onFailure: null,
                        method: HttpMethod.Get,
                        endPointGenerator: () => new Uri(endpoint),
                        requestBodyGenerator: () => null,
                        cancellationToken: cancellation.Token);
                result.Succeeded.ShouldEqual(true);
                result.Attempts.ShouldEqual(2);
                EventMetadata[] responses = tracer.SnapshotRelatedEvents().Select(record => record.Metadata)
                    .Where(metadata => metadata.ContainsKey("availableConnections")).ToArray();
                responses.Length.ShouldEqual(2);
                responses[0]["StatusCode"].ShouldEqual(HttpStatusCode.ServiceUnavailable);
                responses[1]["StatusCode"].ShouldEqual(HttpStatusCode.OK);
                responses[1]["availableConnections"].ShouldEqual(responses[0]["availableConnections"]);
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task ConfigProbeRetainsUnauthorizedStatusWhenItsBodyStalls()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeStalledResponse(listener, cancellation.Token, HttpStatusCode.Unauthorized);
            try
            {
                using ConfigHttpRequestor requestor = new ConfigHttpRequestor(
                    tracer, new MockGVFSEnlistment("mock:\\repo", endpoint, "mock:\\git", new MockGitProcess()),
                    new RetryConfig(0, TimeSpan.FromSeconds(5)) { ReadTimeout = TimeSpan.FromMilliseconds(100) });
                requestor.TryQueryGVFSConfig(false, out ServerGVFSConfig _, out HttpStatusCode? status, out string error)
                    .ShouldEqual(false);
                status.ShouldEqual(HttpStatusCode.Unauthorized);
                error.ShouldContain("401");
                error.ShouldContain("No response body data arrived");
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task FinalResponseDiagnosticFailureReleasesThePreparedResponse()
        {
            using CallbackTracer tracer = new CallbackTracer();
            bool fail = true;
            tracer.Callback = name =>
            {
                if (name == "NetworkResponse" && fail)
                {
                    fail = false;
                    throw new InvalidOperationException("diagnostic failure");
                }
            };
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Uri endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            Task server = ServeRetriedError(listener, cancellation.Token);
            try
            {
                using LoopbackHttpRequestor requestor = new LoopbackHttpRequestor(tracer);
                Assert.Throws<InvalidOperationException>(() => requestor.Get(endpoint));
                using (GitEndPointResponseData response = requestor.Get(endpoint))
                {
                    response.StatusCode.ShouldEqual(HttpStatusCode.OK);
                    response.Stream.ReadByte().ShouldEqual(-1);
                }

                MockTracer.RecordedEvent[] responses = tracer.SnapshotRelatedEvents()
                    .Where(record => record.Name == "NetworkResponse").ToArray();
                responses.Length.ShouldEqual(2);
                responses[1].Metadata["availableConnections"].ShouldEqual(responses[0].Metadata["availableConnections"]);
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase]
        public async Task AnonymousUnauthorizedBodyDoesNotReachTheErrorTelemetry()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Uri endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            Task server = ServeCompleteResponses(listener, cancellation.Token,
                (HttpStatusCode.Unauthorized, "\u001b[31mRAW-BODY-MARKER", "text/plain; charset=utf-8"));
            try
            {
                using GitObjectsHttpRequestor requestor = new GitObjectsHttpRequestor(
                    tracer, new MockGVFSEnlistment(), new CacheServerInfo(endpoint.AbsoluteUri, "test"),
                    new RetryConfig(0, TimeSpan.FromSeconds(5)));
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                    requestor.TrySendProtocolRequest(
                        123,
                        (attempt, response) => throw new InvalidOperationException("Unexpected success"),
                        RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.StandardErrorHandler(tracer, 123, "test"),
                        HttpMethod.Get,
                        () => endpoint,
                        () => null,
                        cancellation.Token);
                result.Succeeded.ShouldEqual(false);
                result.Error.Message.ShouldEqual("Anonymous request was rejected with a 401");
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
                tracer.RelatedErrorEvents[0].ShouldContain("Anonymous request was rejected with a 401");
                tracer.RelatedErrorEvents[0].Contains("RAW-BODY-MARKER", StringComparison.Ordinal).ShouldEqual(false);
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CompletedBadRequestBodyPreservesCharsetAndCredentialPolicy(bool authRequired)
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            string body = authRequired ? HttpRequestor.CacheServerAuthRequiredBadRequestMessage : "denied: caf\u00e9";
            Task server = ServeCompleteResponses(listener, cancellation.Token,
                (HttpStatusCode.Unauthorized, "denied", "text/plain"),
                (HttpStatusCode.OK, "{}", "application/json"),
                (HttpStatusCode.BadRequest, body, "text/plain; charset=utf-8"));
            try
            {
                MockGitProcess git = CreateCredentialGitProcess(endpoint);
                GitAuthentication authentication = new GitAuthentication(git, endpoint);
                MockGVFSEnlistment enlistment = new MockGVFSEnlistment(
                    "mock:\\repo", endpoint, "mock:\\git", git, authentication);
                using LoopbackHttpRequestor requestor = new LoopbackHttpRequestor(tracer, enlistment);
                authentication.TryInitializeAndQueryGVFSConfig(
                    tracer, enlistment, new RetryConfig(0, TimeSpan.FromSeconds(5)),
                    out _, out string error, out _).ShouldEqual(true, error);
                authentication.IsAnonymous.ShouldEqual(false);
                using (GitEndPointResponseData response = requestor.Get(new Uri(endpoint)))
                {
                    response.StatusCode.ShouldEqual(HttpStatusCode.BadRequest);
                    response.ShouldRetry.ShouldEqual(false);
                    response.Error.Message.ShouldContain(body);
                }

                bool rejected = git.CredentialRejections.TryGetValue(endpoint, out System.Collections.Generic.List<MockGitProcess.Credential> rejections);
                rejected.ShouldEqual(authRequired);
                if (authRequired)
                {
                    rejections.Count.ShouldEqual(1);
                }
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        [TestCase]
        public async Task AuthenticationInitializationLoadsAllRetrySettings()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeCompleteResponses(listener, cancellation.Token,
                (HttpStatusCode.OK, "{}", "application/json"));
            try
            {
                MockGitProcess git = CreateCredentialGitProcess(endpoint);
                git.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result("0", string.Empty, 0));
                git.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result("2", string.Empty, 0));
                git.SetExpectedCommandResult("config gvfs.read-timeout-seconds", () => new GitProcess.Result("1", string.Empty, 0));
                GitAuthentication authentication = new GitAuthentication(git, endpoint);
                MockGVFSEnlistment enlistment = new MockGVFSEnlistment(
                    "mock:\\repo", endpoint, "mock:\\git", git, authentication);

                // Both config readers must use the same isolated settings.
                using IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config();
                File.WriteAllText(machineConfig.GlobalConfigPath, "[gvfs]\n\tmax-retries = 0\n\ttimeout-seconds = 2\n\tread-timeout-seconds = 1\n");
                authentication.TryInitialize(tracer, enlistment, out string error).ShouldEqual(true, error);
                EventMetadata loaded = tracer.RelatedEventMetadata[tracer.RelatedEventNames.IndexOf("RetryConfig_LoadedRetryConfig")];
                loaded["MaxRetries"].ShouldEqual(0);
                loaded["Timeout"].ShouldEqual(TimeSpan.FromSeconds(2));
                loaded["ReadTimeout"].ShouldEqual(TimeSpan.FromSeconds(1));
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                await ObserveServerCancellation(server, cancellation.Token);
            }
        }

        private static MockGitProcess CreateCredentialGitProcess(string endpoint)
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult("config --get-urlmatch http " + endpoint,
                () => new GitProcess.Result(string.Empty, string.Empty, 0));
            git.SetExpectedCommandResult("config gvfs.max-http-connections",
                () => new GitProcess.Result(string.Empty, string.Empty, 1));
            const string Prefix = "-c credential.\"https://dev.azure.com\".useHttpPath=true credential ";
            git.SetExpectedCommandResult(Prefix + "fill",
                () => new GitProcess.Result("username=test-user\npassword=test-password\n", string.Empty, 0));
            git.SetExpectedCommandResult(Prefix + "approve", () => new GitProcess.Result(string.Empty, string.Empty, 0));
            git.SetExpectedCommandResult(Prefix + "reject", () => new GitProcess.Result(string.Empty, string.Empty, 0));
            return git;
        }

        private static async Task ServeCompleteResponses(TcpListener listener, CancellationToken cancellationToken,
            params (HttpStatusCode Status, string Body, string ContentType)[] responses)
        {
            foreach ((HttpStatusCode status, string body, string contentType) in responses)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream stream = client.GetStream();
                await ReadRequestHeaders(stream, cancellationToken);
                byte[] data = Encoding.UTF8.GetBytes(body);
                byte[] headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(int)status} Test\r\nConnection: close\r\nContent-Type: {contentType}\r\nContent-Length: {data.Length}\r\n\r\n");
                await stream.WriteAsync(headers.AsMemory(), cancellationToken);
                await stream.WriteAsync(data.AsMemory(), cancellationToken);
            }
        }

        private static async Task ObserveServerCancellation(Task server, CancellationToken cancellationToken)
        {
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private static async Task ServeRetriedError(TcpListener listener, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream stream = client.GetStream();
                await ReadRequestHeaders(stream, cancellationToken);
                byte[] response = Encoding.ASCII.GetBytes(attempt == 0
                    ? "HTTP/1.1 503 Unavailable\r\nContent-Length: 2\r\n\r\nx"
                    : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
                await stream.WriteAsync(response.AsMemory(), cancellationToken);
                if (attempt == 0)
                {
                    (await stream.ReadAsync(new byte[1].AsMemory(), cancellationToken)).ShouldEqual(0);
                }
            }
        }

        private static async Task ServeStalledResponse(
            TcpListener listener,
            CancellationToken cancellationToken,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = connection.GetStream();
            await ReadRequestHeaders(stream, cancellationToken);
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)status} Test\r\nContent-Length: 2\r\nX-Cache-Name: test-cache\r\n\r\nx");
            await stream.WriteAsync(response.AsMemory(), cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        private static async Task ReadRequestHeaders(NetworkStream stream, CancellationToken cancellationToken)
        {
            using StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string line;
            do
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            while (!string.IsNullOrEmpty(line));
        }

        private class LoopbackHttpRequestor : HttpRequestor
        {
            public LoopbackHttpRequestor(MockTracer tracer, Enlistment enlistment = null)
                : base(
                    tracer,
                    new RetryConfig(0, TimeSpan.FromSeconds(5)) { ReadTimeout = TimeSpan.FromMilliseconds(300) },
                    enlistment ?? new MockGVFSEnlistment())
            {
            }

            public GitEndPointResponseData Get(Uri uri)
            {
                return this.SendRequest(42, uri, HttpMethod.Get, null, CancellationToken.None);
            }
        }
    }
}
