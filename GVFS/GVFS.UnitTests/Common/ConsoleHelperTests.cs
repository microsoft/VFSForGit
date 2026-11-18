using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    [NonParallelizable]
    public class ConsoleHelperTests
    {
        private const bool OriginalWriter = false;
        private const bool ReplacedWriter = true;
        private const string UnknownMetadata = "unknown";

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ConsoleEligibilityUsesTheSelectedStream(bool isOutputRedirected, bool isErrorRedirected)
        {
            using (StringWriter stdout = new StringWriter())
            using (StringWriter stderr = new StringWriter())
            using (StringWriter other = new StringWriter())
            {
                ConsoleHelper.ShouldShowSpinner(true, stdout, stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(!isOutputRedirected);
                ConsoleHelper.ShouldShowSpinner(true, stderr, stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(!isErrorRedirected);
                ConsoleHelper.ShouldShowSpinner(true, other, stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(false);
                ConsoleHelper.ShouldShowSpinner(true, TextWriter.Synchronized(stdout), stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(false);
                ConsoleHelper.ShouldShowSpinner(false, stdout, stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(false);
                ConsoleHelper.ShouldShowSpinner(false, stderr, stdout, stderr, isOutputRedirected, isErrorRedirected)
                    .ShouldEqual(false);
            }
        }

        [TestCase("s_isOutTextWriterRedirected")]
        [TestCase("s_isErrorTextWriterRedirected")]
        public void ConsoleRedirectionMetadataMatchesRuntime(string name)
        {
            FieldInfo field = typeof(Console).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Console redirection metadata changed.");
            field.FieldType.ShouldEqual(typeof(bool));
        }

        [TestCase(null, true)]
        [TestCase(nameof(UnknownMetadata), true)]
        [TestCase(nameof(OriginalWriter), false)]
        [TestCase(nameof(ReplacedWriter), true)]
        public void ConsoleWriterMetadataFailsClosed(string name, bool expected)
        {
            FieldInfo field = name == null ? null : typeof(ConsoleHelperTests).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            ConsoleHelper.IsConsoleWriterReplaced(field).ShouldEqual(expected);
        }

        [TestCase(ConsoleHelper.ActionResult.Success, "Succeeded")]
        [TestCase(ConsoleHelper.ActionResult.CompletedWithErrors, "Completed with errors.")]
        [TestCase(ConsoleHelper.ActionResult.Failure, "Failed")]
        public void StringWriterGetsStaticStatus(ConsoleHelper.ActionResult result, string expectedResult)
        {
            using (StringWriter output = new StringWriter())
            {
                this.WriteStatus(output, result);
                output.ToString().ShouldEqual("Probe..." + expectedResult + Environment.NewLine);
            }
        }

        [TestCase]
        public void MemoryStreamGetsStaticStatus()
        {
            using (MemoryStream stream = new MemoryStream())
            using (StreamWriter output = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                this.WriteStatus(output);
                output.Flush();
                Encoding.UTF8.GetString(stream.ToArray()).ShouldEqual("Probe...Succeeded" + Environment.NewLine);
            }
        }

        [TestCase]
        public void FileStreamWriterGetsStaticStatus()
        {
            using (AnonymousPipeServerStream server = new AnonymousPipeServerStream(PipeDirection.Out))
            using (AnonymousPipeClientStream client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle))
            using (StreamReader reader = new StreamReader(client))
            {
                // A pipe handle exercises FileStream without disk I/O.
                using (SafeFileHandle handle = new SafeFileHandle(server.SafePipeHandle.DangerousGetHandle(), ownsHandle: false))
                using (FileStream stream = new FileStream(handle, FileAccess.Write))
                using (StreamWriter output = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    this.WriteStatus(output);
                }

                server.Dispose();
                reader.ReadToEnd().ShouldEqual("Probe...Succeeded" + Environment.NewLine);
            }
        }

        [TestCase]
        public void PipeGetsStaticStatus()
        {
            using (AnonymousPipeServerStream server = new AnonymousPipeServerStream(PipeDirection.Out))
            using (AnonymousPipeClientStream client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle))
            using (StreamReader reader = new StreamReader(client))
            {
                using (StreamWriter output = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    this.WriteStatus(output);
                }

                server.Dispose();
                reader.ReadToEnd().ShouldEqual("Probe...Succeeded" + Environment.NewLine);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManagedConsoleReplacementIsNotInteractive(bool useError)
        {
            TextWriter original = useError ? Console.Error : Console.Out;
            using (StringWriter output = new StringWriter())
            {
                try
                {
                    if (useError)
                    {
                        Console.SetError(output);
                    }
                    else
                    {
                        Console.SetOut(output);
                    }

                    TextWriter replacement = useError ? Console.Error : Console.Out;
                    ConsoleHelper.ShouldShowSpinner(true, replacement, isOutputRedirected: false, isErrorRedirected: false)
                        .ShouldEqual(false);
                    this.WriteStatus(replacement);
                    output.ToString().ShouldEqual("Probe...Succeeded" + Environment.NewLine);
                }
                finally
                {
                    if (useError)
                    {
                        Console.SetError(original);
                    }
                    else
                    {
                        Console.SetOut(original);
                    }
                }
            }
        }

        [TestCase]
        public void ExplicitlyDisabledSpinnerKeepsStaticStatus()
        {
            using (StringWriter output = new StringWriter())
            {
                ConsoleHelper.ShowStatusWhileRunning(
                    () =>
                    {
                        output.ToString().ShouldEqual("Probe...");
                        return true;
                    },
                    "Probe",
                    output,
                    showSpinner: false,
                    gvfsLogEnlistmentRoot: null,
                    initialDelayMs: Timeout.Infinite).ShouldEqual(true);

                output.ToString().ShouldEqual("Probe...Succeeded" + Environment.NewLine);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ExceptionKeepsStaticFailureAndLogHint()
        {
            using (StringWriter output = new StringWriter())
            {
                Assert.Throws<InvalidOperationException>(
                    () => ConsoleHelper.ShowStatusWhileRunning(
                        (Func<bool>)(() => { throw new InvalidOperationException("Probe failed"); }),
                        getMessage: () => "Dynamic",
                        "Probe",
                        output,
                        showSpinner: true,
                        gvfsLogEnlistmentRoot: "repo",
                        initialDelayMs: Timeout.Infinite));

                output.ToString().ShouldEqual("Probe...Failed. Run 'gvfs log repo' for more info." + Environment.NewLine);
            }
        }

        private void WriteStatus(TextWriter output, ConsoleHelper.ActionResult result = ConsoleHelper.ActionResult.Success)
        {
            int progressCalls = 0;
            ConsoleHelper.ShowStatusWhileRunning(
                () => result,
                getMessage: () =>
                {
                    Interlocked.Increment(ref progressCalls);
                    return "Dynamic";
                },
                "Probe",
                output,
                showSpinner: true,
                gvfsLogEnlistmentRoot: null,
                initialDelayMs: Timeout.Infinite).ShouldEqual(result);
            progressCalls.ShouldEqual(0);
        }
    }
}
