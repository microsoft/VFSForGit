using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    [NonParallelizable]
    public class PrefetchVerbTests
    {
        [TestCase("legacy")]
        [TestCase("progress")]
        [TestCase("unavailable")]
        [TestCase("disconnect")]
        [TestCase("failure")]
        [Category(CategoryConstants.ExceptionExpected)]
        [Timeout(15000)]
        public async Task MountProgressPreservesCompletionFallbackAndRedirectedOutput(string scenario)
        {
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            using MockTracer tracer = new MockTracer();
            using StringWriter output = new StringWriter();
            PrefetchVerb verb = new PrefetchVerb { Output = output };
            using NamedPipeServerStream server = new NamedPipeServerStream(
                enlistment.NamedPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            NamedPipeMessages.Message request = null;
            Task serve = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                NamedPipeStreamReader reader = new NamedPipeStreamReader(server);
                NamedPipeStreamWriter writer = new NamedPipeStreamWriter(server);
                request = NamedPipeMessages.Message.FromString(reader.ReadMessage());
                if (scenario == "unavailable")
                {
                    writer.WriteMessage(new NamedPipeMessages.Message("MountNotReady", null).ToString());
                    return;
                }

                if (scenario != "legacy")
                {
                    writer.WriteMessage(new NamedPipeMessages.Message("PrefetchCommitsStarted", null).ToString());
                    writer.WriteMessage(new NamedPipeMessages.Message("PrefetchCommitsProgress", "Resolving deltas: 42%").ToString());
                    if (scenario == "disconnect")
                    {
                        server.Disconnect();
                        return;
                    }
                }

                writer.WriteMessage(new NamedPipeMessages.PrefetchCommits.Response
                {
                    Success = scenario != "failure",
                    Error = scenario == "failure" ? "Synthetic index failure" : null,
                }.CreateMessage().ToString());
            });

            try
            {
                if (scenario == "disconnect" || scenario == "failure")
                {
                    Assert.Throws<GVFSVerb.VerbAbortedException>(() => verb.TryPrefetchCommitsViaMountProcess(tracer, enlistment));
                    Assert.That(verb.ReturnCode, Is.EqualTo(ReturnCode.GenericError));
                    Assert.That(output.ToString(), Does.Contain(
                        scenario == "failure" ? "Synthetic index failure" : "Lost progress connection"));
                }
                else
                {
                    Assert.That(verb.TryPrefetchCommitsViaMountProcess(tracer, enlistment),
                        Is.EqualTo(scenario != "unavailable"));
                }

                await serve.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(request.Header, Is.EqualTo("PrefetchCommits"));
                Assert.That(request.Body, Is.EqualTo("ReportProgress"));
                Assert.That(Regex.IsMatch(output.ToString(), @"\r(?!\n)"), Is.False);
                Assert.That(output.ToString(), Does.Not.Contain("Resolving deltas: 42%"));
                if (scenario == "progress")
                {
                    Assert.That(output.ToString(), Is.EqualTo("Fetching commits and trees...Succeeded" + Environment.NewLine));
                }
            }
            finally
            {
                server.Dispose();
                await serve.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
