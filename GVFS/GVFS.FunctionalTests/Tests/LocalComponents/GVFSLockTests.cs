using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    public class GVFSLockTests
    {
        [TestCase("MountNotReady", "not finished initializing")]
        [TestCase("UnmountInProgress", "unmounting")]
        public async Task MountStateChangeStopsLockRetriesOverThePipe(string mountState, string expectedError)
        {
            string pipeName = "GVFS_test_lock_retry_" + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                using CancellationTokenRegistration abort = deadline.Token.Register(() => server.Dispose());
                Task serve = Task.Run(async () =>
                {
                    await server.WaitForConnectionAsync(deadline.Token);
                    NamedPipeStreamReader reader = new NamedPipeStreamReader(server);
                    NamedPipeStreamWriter writer = new NamedPipeStreamWriter(server);
                    NamedPipeMessages.Message denial = new NamedPipeMessages.AcquireLock.Response(
                        "LockDeniedGVFS", denyGVFSMessage: "Waiting for GVFS").CreateMessage();

                    foreach (NamedPipeMessages.Message response in new[]
                    {
                        denial,
                        new NamedPipeMessages.Message(mountState, null),
                    })
                    {
                        NamedPipeMessages.Message request = NamedPipeMessages.Message.FromString(reader.ReadMessage());
                        request.Header.ShouldEqual("AcquireLock");
                        NamedPipeMessages.LockData data = new NamedPipeMessages.LockRequest(request.Body).RequestData;
                        data.CheckAvailabilityOnly.ShouldBeTrue();
                        data.ParsedCommand.ShouldEqual("git status --serialize=mock:\\status");
                        writer.WriteMessage(response.ToString());
                    }

                    reader.ReadMessage().ShouldBeNull("The hook sent another request after the mount state changed.");
                });

                try
                {
                    using (NamedPipeClient client = new NamedPipeClient(pipeName))
                    {
                        client.Connect().ShouldBeTrue();
                        GVFSLock.TryAcquireGVFSLockForProcess(
                            unattended: true, client, "git status --serialize=mock:\\status", 123, false, true,
                            checkAvailabilityOnly: true, gvfsEnlistmentRoot: null, gitCommandSessionId: string.Empty,
                            out string error).ShouldBeFalse();
                        error.ShouldContain(expectedError);
                    }

                    await serve.WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    deadline.Cancel();
                    try
                    {
                        await serve.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                    {
                    }
                    catch (ObjectDisposedException) when (deadline.IsCancellationRequested)
                    {
                    }
                    catch (IOException) when (deadline.IsCancellationRequested)
                    {
                    }
                }
            }
        }
    }
}
