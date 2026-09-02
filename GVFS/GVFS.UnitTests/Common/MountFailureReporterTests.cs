using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    /// <summary>
    /// Covers the server half of the mount-failure handshake. Before this state machine
    /// was extracted from InProcessMount it lived in GVFS.Mount, which GVFS.UnitTests
    /// cannot reference, so none of it was covered.
    /// </summary>
    [TestFixture]
    public class MountFailureReporterTests
    {
        // Deliberately much smaller than the production defaults: these only have to be
        // far enough apart to tell the two timeout paths apart, and one test has to wait
        // the long one out in real time.
        private const int ReportTimeoutMs = 600;
        private const int NoClientTimeoutMs = 200;
        private const int DrainMs = 20;

        [TestCase]
        public void FirstThreadTakesOwnershipAndPublishesTheMessage()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.FailureMessage.ShouldBeNull();

                reporter.TryTakeOwnership("the real reason").ShouldEqual(MountFailureOwnership.Owner);
                reporter.FailureMessage.ShouldEqual("the real reason");
            }
        }

        [TestCase]
        public void SameThreadCallingTwiceIsReentrantRatherThanBlocked()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.TryTakeOwnership("first").ShouldEqual(MountFailureOwnership.Owner);

                // A throw inside the reporting path can make an outer catch block call
                // back in on the same thread. That must not be treated as a second thread,
                // or the caller would park forever holding the mount lock and the pipe.
                reporter.TryTakeOwnership("second").ShouldEqual(MountFailureOwnership.Reentrant);

                // The re-entrant call must not overwrite the reason already published.
                reporter.FailureMessage.ShouldEqual("first");
            }
        }

        [TestCase]
        public void SecondThreadIsNotTheOwner()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.TryTakeOwnership("first").ShouldEqual(MountFailureOwnership.Owner);

                MountFailureOwnership fromOtherThread = MountFailureOwnership.Owner;
                Task other = Task.Run(() => fromOtherThread = reporter.TryTakeOwnership("second"));
                other.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();

                fromOtherThread.ShouldEqual(MountFailureOwnership.NotOwner);
                reporter.FailureMessage.ShouldEqual("first");
            }
        }

        [TestCase]
        public void WaitReturnsImmediatelyWhenThePipeNeverOpened()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.TryTakeOwnership("failed before the pipe opened");

                // No client can read the failure, so the process must not linger holding
                // the mount lock. The caller falls back to the process exit code.
                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(NoClientTimeoutMs));
            }
        }

        [TestCase]
        public void WaitUsesTheShortTimeoutWhenNoClientHasReceivedAStatus()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.OnNamedPipeReady();
                reporter.TryTakeOwnership("nobody is listening");

                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                // Should give up after the no-client timeout, well short of the full one.
                Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThan(NoClientTimeoutMs / 2));
                Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(ReportTimeoutMs));
            }
        }

        [TestCase]
        public void WaitReturnsOnceAClientReadsTheFailure()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.OnNamedPipeReady();
                reporter.TryTakeOwnership("the real reason");
                reporter.OnStatusDelivered(carriedMountFailure: true);

                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                // Returns as soon as the failure was delivered, plus the drain.
                Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(NoClientTimeoutMs));
            }
        }

        [TestCase]
        public void DeliveringANonFailureStatusDoesNotReleaseTheWait()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.OnNamedPipeReady();

                // A client polled while the mount was still in progress. That proves
                // somebody is listening, but it did not carry the failure.
                reporter.OnStatusDelivered(carriedMountFailure: false);
                reporter.TryTakeOwnership("the real reason");

                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                // Must wait for the failure itself to be read, using the longer timeout
                // because a client is known to be polling. Asserting above the SHORT
                // timeout is what proves the long path was selected.
                Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThan(NoClientTimeoutMs * 2));
            }
        }

        [TestCase]
        public void WaitIsReleasedByAClientThatReadsTheFailureLater()
        {
            using (MountFailureReporter reporter = CreateReporter())
            {
                reporter.OnNamedPipeReady();
                reporter.OnStatusDelivered(carriedMountFailure: false);
                reporter.TryTakeOwnership("the real reason");

                Task client = Task.Run(() =>
                {
                    Thread.Sleep(50);
                    reporter.OnStatusDelivered(carriedMountFailure: true);
                });

                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                client.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();

                // Released by the delivery, not by the timeout.
                Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(ReportTimeoutMs));
            }
        }

        [TestCase]
        public void ProductionDefaultsSelectTheShorterTimeoutWhenNoClientIsListening()
        {
            // Everything else builds the reporter through the internal test constructor,
            // so this is the only cover for the public constructor and its forwarding of
            // the Default* constants. Without it, swapping DefaultReportTimeoutMs and
            // DefaultNoClientTimeoutMs would ship unnoticed.
            using (MountFailureReporter reporter = new MountFailureReporter(new MockTracer()))
            {
                reporter.OnNamedPipeReady();
                reporter.TryTakeOwnership("nobody is listening");

                Stopwatch elapsed = Stopwatch.StartNew();
                reporter.WaitForClientToReadFailure();
                elapsed.Stop();

                // The no-client timeout must be the one used, and it must be meaningfully
                // shorter than the timeout used once a client is known to be polling.
                Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThan(MountFailureReporter.DefaultNoClientTimeoutMs / 2));
                Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(MountFailureReporter.DefaultReportTimeoutMs));
            }
        }

        [TestCase]
        public void ProductionDefaultsAreOrderedSensibly()
        {
            // A failed mount holds the mount lock and the named pipe until it exits, so
            // these bound how long a user's next "gvfs mount" is delayed.
            Assert.That(
                MountFailureReporter.DefaultNoClientTimeoutMs,
                Is.LessThan(MountFailureReporter.DefaultReportTimeoutMs),
                "Waiting for a client that has never polled must not take longer than waiting for one that has.");

            // The drain has to outlast the client's 100ms GetStatus poll interval, or a
            // concurrent "gvfs status" can consume the report signal and the mounting
            // client never gets its answer.
            Assert.That(
                MountFailureReporter.DefaultDrainMs,
                Is.GreaterThan(100),
                "The drain must exceed the client's GetStatus poll interval.");
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
            // A pre-built message can legitimately contain braces, so it must not be run
            // through string.Format when there is nothing to substitute.
            MountFailureReporter.FormatFailure("a message with {braces} in it", null)
                .ShouldEqual("a message with {braces} in it");

            MountFailureReporter.FormatFailure("a message with {braces} in it", new object[0])
                .ShouldEqual("a message with {braces} in it");
        }

        [TestCase]
        public void FormatFailureFallsBackWhenTheFormatStringIsMalformed()
        {
            // Failure text is built from exception messages and server responses, which
            // can contain unbalanced braces. Formatting must never throw and mask the
            // failure being reported.
            string result = MountFailureReporter.FormatFailure("unbalanced {0} and {", new object[] { "value" });

            result.ShouldEqual("unbalanced {0} and {");
        }

        private static MountFailureReporter CreateReporter()
        {
            return new MountFailureReporter(new MockTracer(), ReportTimeoutMs, NoClientTimeoutMs, DrainMs);
        }
    }
}
