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
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    [NonParallelizable]
    public class PrefetchVerbTests
    {
        [TestCase(80)]
        [TestCase(120)]
        public void ConsoleProgressFitsTheLineAndKeepsNativePercentageFirst(int width)
        {
            const string Message = "Fetching commits and trees from cache server";
            string status = "Resolving deltas: 42% [pack 1]; Downloaded 43/43 packs; indexes ready 42/43; indexing 1; elapsed 00:18:34";
            string rendered = PrefetchVerb.FormatConsoleProgress(status, Message, width);
            Assert.That(rendered, Does.StartWith("Resolving deltas: 42%"));
            Assert.That(rendered.Length + Message.Length + 8, Is.LessThanOrEqualTo(width));
        }

        [TestCase]
        public void ConsoleProgressBoundsWideCharactersAndHandlesVeryNarrowWindows()
        {
            Assert.That(PrefetchVerb.FormatConsoleProgress(new string('\u754c', 40), "Fetching commits and trees", 80),
                Is.EqualTo(new string('\u754c', 21) + "..."));
            Assert.That(PrefetchVerb.FormatConsoleProgress("Indexing objects: 42%", "Fetching commits and trees", 10), Is.Null);
        }

        [TestCase("completionOnlyMount", false)]
        [TestCase("progress", false)]
        [TestCase("progress", true)]
        [TestCase("unavailable", false)]
        [TestCase("disconnect", false)]
        [TestCase("failure", false)]
        [TestCase("unexpected", false)]
        [TestCase("emptyCompletion", false)]
        [Category(CategoryConstants.ExceptionExpected)]
        [Timeout(15000)]
        public async Task MountProgressPreservesCompletionFallbackAndRedirectedOutput(string scenario, bool verbose)
        {
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            using MockTracer tracer = new MockTracer();
            using StringWriter output = new StringWriter();
            PrefetchVerb verb = new PrefetchVerb { Output = output, Verbose = verbose };
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using NamedPipeServerStream server = new NamedPipeServerStream(
                enlistment.NamedPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            NamedPipeMessages.Message request = null;
            Task serve = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync(cancellation.Token);
                NamedPipeStreamReader reader = new NamedPipeStreamReader(server);
                NamedPipeStreamWriter writer = new NamedPipeStreamWriter(server);
                request = NamedPipeMessages.Message.FromString(reader.ReadMessage());
                if (scenario == "unavailable")
                {
                    writer.WriteMessage(new NamedPipeMessages.Message("MountNotReady", null).ToString());
                    return;
                }

                if (scenario != "completionOnlyMount")
                {
                    writer.WriteMessage(new NamedPipeMessages.Message("PrefetchCommitsStarted", null).ToString());
                    writer.WriteMessage(new NamedPipeMessages.Message("PrefetchCommitsProgress", "Resolving deltas: 42%").ToString());
                    if (scenario == "disconnect")
                    {
                        server.Disconnect();
                        return;
                    }

                    if (scenario == "unexpected" || scenario == "emptyCompletion")
                    {
                        writer.WriteMessage(scenario == "unexpected" ? "UnexpectedHeader" : "PrefetchCommitsComplete|null");
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
                if (scenario == "disconnect" || scenario == "failure" || scenario == "unexpected" || scenario == "emptyCompletion")
                {
                    Assert.Throws<GVFSVerb.VerbAbortedException>(() => verb.TryPrefetchCommitsViaMountProcess(tracer, enlistment));
                    Assert.That(verb.ReturnCode, Is.EqualTo(ReturnCode.GenericError));
                    string expectedError = scenario switch
                    {
                        "failure" => "Synthetic index failure",
                        "unexpected" => "Unexpected prefetch progress response from the mount: UnexpectedHeader.",
                        "emptyCompletion" => "The mount returned an empty prefetch response.",
                        _ => "The mount closed the prefetch progress connection.",
                    };
                    Assert.That(output.ToString(), Does.Contain(expectedError));
                    if (scenario != "failure")
                    {
                        Assert.That(output.ToString(), Does.Contain("The mount can still run prefetch.").And.Contain("gvfs log"));
                    }
                }
                else
                {
                    Assert.That(verb.TryPrefetchCommitsViaMountProcess(tracer, enlistment),
                        Is.EqualTo(scenario != "unavailable"));
                }

                await serve.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Header, Is.EqualTo("PrefetchCommits"));
                Assert.That(request.Body, Is.EqualTo("ReportProgress"));
                Assert.That(Regex.IsMatch(output.ToString(), @"\r(?!\n)"), Is.False);
                Assert.That(output.ToString(), Does.Not.Contain("Resolving deltas: 42%"));
                if (scenario == "progress" && !verbose)
                {
                    Assert.That(output.ToString(), Is.EqualTo("Fetching commits and trees...Succeeded" + Environment.NewLine));
                }
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await serve.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
        }
    }
}
