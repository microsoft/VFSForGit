using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class MountFailureReporterTests
    {
        [TestCase]
        public void FirstCallerOwnsTheFailureAndPublishesTheMessage()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);

            reporter.FailureMessage.ShouldBeNull();

            reporter.TryBeginFailure(exitCode: 3, message: "the real reason").ShouldBeTrue();

            reporter.FailureMessage.ShouldEqual("the real reason");
            environment.ExitCodes.Count.ShouldEqual(0);
            environment.BlockCount.ShouldEqual(0);
        }

        [TestCase]
        public void ReentrantCallOnTheOwningThreadExitsAndKeepsTheFirstMessage()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.TryBeginFailure(exitCode: 3, message: "first").ShouldBeTrue();

            reporter.TryBeginFailure(exitCode: 7, message: "second").ShouldBeFalse();

            environment.ExitCodes.ShouldMatchInOrder(7);
            environment.BlockCount.ShouldEqual(0);
            reporter.FailureMessage.ShouldEqual("first");
        }

        [TestCase]
        public void CallFromAnotherThreadBlocksUntilTheProcessExits()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.TryBeginFailure(exitCode: 3, message: "first").ShouldBeTrue();

            bool otherThreadResult = true;
            Task other = Task.Run(() => otherThreadResult = reporter.TryBeginFailure(exitCode: 7, message: "second"));
            other.Wait(Timeout.Infinite);

            otherThreadResult.ShouldBeFalse();
            environment.BlockCount.ShouldEqual(1);
            environment.ExitCodes.Count.ShouldEqual(0);
            reporter.FailureMessage.ShouldEqual("first");
        }

        [TestCase]
        public void WaitDoesNothingWhenThePipeNeverOpened()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.TryBeginFailure(exitCode: 3, message: "failed before the pipe opened");

            reporter.WaitForClientToReadFailure().ShouldBeFalse();

            environment.WaitTimeouts.Count.ShouldEqual(0);
            environment.Sleeps.Count.ShouldEqual(0);
        }

        [TestCase]
        public void WaitUsesTheShortTimeoutWhenNoClientHasReceivedAStatus()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.OnNamedPipeReady();
            reporter.TryBeginFailure(exitCode: 3, message: "nobody is listening");

            reporter.WaitForClientToReadFailure().ShouldBeFalse();

            environment.WaitTimeouts.ShouldMatchInOrder(MountFailureReporter.DefaultNoClientTimeoutMs);
            environment.Sleeps.Count.ShouldEqual(0);
        }

        [TestCase]
        public void WaitUsesTheLongTimeoutOnceAClientHasReceivedAStatus()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.OnNamedPipeReady();

            // A client polled while the mount was still in progress. Somebody is listening,
            // but that response did not carry the failure.
            reporter.TrySendStatus(() => true, carriesMountFailure: false).ShouldBeTrue();
            reporter.TryBeginFailure(exitCode: 3, message: "the real reason");

            reporter.WaitForClientToReadFailure().ShouldBeFalse();

            environment.WaitTimeouts.ShouldMatchInOrder(MountFailureReporter.DefaultReportTimeoutMs);
        }

        [TestCase]
        public void WaitReturnsAndDrainsOnceAClientHasReadTheFailure()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.OnNamedPipeReady();
            reporter.TryBeginFailure(exitCode: 3, message: "the real reason");
            reporter.TrySendStatus(() => true, carriesMountFailure: true).ShouldBeTrue();

            reporter.WaitForClientToReadFailure().ShouldBeTrue();

            environment.Sleeps.ShouldMatchInOrder(MountFailureReporter.DefaultDrainMs);
        }

        [TestCase]
        public void AFailedSendIsNotRecordedAndDoesNotReleaseTheWait()
        {
            FakeEnvironment environment = new FakeEnvironment();
            MountFailureReporter reporter = CreateReporter(environment);
            reporter.OnNamedPipeReady();
            reporter.TryBeginFailure(exitCode: 3, message: "the real reason");

            // The client disconnected before it received the response. Counting it would
            // release the wait and let the process exit before any client read the failure.
            reporter.TrySendStatus(() => false, carriesMountFailure: true).ShouldBeFalse();

            reporter.WaitForClientToReadFailure().ShouldBeFalse();

            environment.WaitTimeouts.ShouldMatchInOrder(MountFailureReporter.DefaultNoClientTimeoutMs);
            environment.Sleeps.Count.ShouldEqual(0);
        }

        [TestCase]
        public void DrainOutlastsTheClientPollIntervalAndTheNoClientWaitIsShorter()
        {
            // The mounting client must get another poll before the process exits, even if a
            // different client consumed the failure first.
            MountFailureReporter.DefaultDrainMs.ShouldBeAtLeast(GVFSEnlistment.MountStatusPollIntervalMs + 1);

            MountFailureReporter.DefaultNoClientTimeoutMs.ShouldBeAtMost(MountFailureReporter.DefaultReportTimeoutMs - 1);
        }

        [TestCase]
        public void FormatFailureAppliesArguments()
        {
            MountFailureReporter.FormatFailure("Error: {0}", new object[] { "boom" })
                .ShouldEqual("Error: boom");
        }

        [TestCase]
        public void FormatFailureLeavesTheMessageAloneWhenThereAreNoArguments()
        {
            // A pre-built message can contain braces, so it must not be run through
            // string.Format when there is nothing to substitute.
            MountFailureReporter.FormatFailure("a message with {braces} in it", null)
                .ShouldEqual("a message with {braces} in it");

            MountFailureReporter.FormatFailure("a message with {braces} in it", new object[0])
                .ShouldEqual("a message with {braces} in it");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FormatFailureFallsBackWhenTheFormatStringIsMalformed()
        {
            // Failure text built from exception messages and server responses can contain
            // unbalanced braces. Formatting must not throw and mask the reported failure.
            MountFailureReporter.FormatFailure("unbalanced {0} and {", new object[] { "value" })
                .ShouldEqual("unbalanced {0} and {");
        }

        private static MountFailureReporter CreateReporter(FakeEnvironment environment)
        {
            return new MountFailureReporter(new MockTracer(), environment);
        }

        private class FakeEnvironment : IMountFailureEnvironment
        {
            public List<int> WaitTimeouts { get; } = new List<int>();

            public List<int> Sleeps { get; } = new List<int>();

            public List<int> ExitCodes { get; } = new List<int>();

            public int BlockCount { get; private set; }

            public bool WaitForSignal(WaitHandle signal, int timeoutMs)
            {
                this.WaitTimeouts.Add(timeoutMs);

                // Report the signal's current state instead of waiting on it.
                return signal.WaitOne(0);
            }

            public void Sleep(int durationMs)
            {
                this.Sleeps.Add(durationMs);
            }

            public void ExitProcess(int exitCode)
            {
                this.ExitCodes.Add(exitCode);
            }

            public void BlockUntilProcessExits()
            {
                this.BlockCount++;
            }
        }
    }
}
