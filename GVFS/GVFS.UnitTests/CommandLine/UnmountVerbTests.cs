using GVFS.CommandLine;
using GVFS.Common.NamedPipes;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class UnmountVerbTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Timeout(15000)]
        public async Task ReportsPrefetchWaitAndTimeoutWithoutChangingLegacyCompletion(bool progress, bool timeout)
        {
            string pipeName = "unmount-test-" + Guid.NewGuid().ToString("N");
            using StringWriter output = new StringWriter();
            UnmountVerb verb = new UnmountVerb { Output = output };
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using NamedPipeServerStream server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Task serve = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(cancellation.Token);
                NamedPipeStreamReader reader = new NamedPipeStreamReader(server);
                NamedPipeStreamWriter writer = new NamedPipeStreamWriter(server);
                Assert.That(reader.ReadMessage(), Is.EqualTo(NamedPipeMessages.GetStatus.Request));
                writer.WriteMessage(new NamedPipeMessages.GetStatus.Response { MountStatus = NamedPipeMessages.GetStatus.Ready }.ToJson());
                Assert.That(reader.ReadMessage(), Is.EqualTo("Unmount|ReportProgress"));
                writer.WriteMessage(NamedPipeMessages.Unmount.Acknowledged);
                if (progress)
                {
                    writer.WriteMessage("UnmountWaiting|Waiting for commit prefetch (requester PID 1234, mount PID 5678).");
                }

                writer.WriteMessage(timeout
                    ? "UnmountTimedOut|The repository stays mounted. Waiting for commit prefetch (requester PID 1234, mount PID 5678)."
                    : NamedPipeMessages.Unmount.Completed);
            });

            Assert.That(verb.Unmount(pipeName, out string error), Is.EqualTo(!timeout));
            await serve.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(output.ToString().Contains("requester PID 1234"), Is.EqualTo(progress));
            if (timeout)
            {
                Assert.That(error, Does.Contain("stays mounted").And.Contain("requester PID 1234"));
            }
            else
            {
                Assert.That(error, Is.Empty);
            }
        }
    }
}
