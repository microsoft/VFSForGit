using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitConfigReadRetryTests
    {
        [TestCase("warning: unable to access '.git/config': Permission denied", true)]
        [TestCase("warning: unable to access '.git/config': Permission denied\r\nfatal: unknown error occurred while reading the configuration files", true)]
        [TestCase("could not open 'C:\\repo\\.git\\config': The process cannot access the file because it is being used by another process.", true)]
        [TestCase("error: unable to access 'C:/repo/.git/config': Device or resource busy", true)]
        [TestCase("fatal: unable to access 'C:/Permission denied/.git/config': No such file or directory", false)]
        [TestCase("error: could not open 'C:/Device or resource busy/config': The system cannot find the file specified.", false)]
        [TestCase("Permission denied", false)]
        [TestCase("fatal: bad config line 4 in file .git/config", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsTransientFileAccessErrorMatchesOnlyTheFailureReason(string error, bool expected)
        {
            GitConfigReadRetry.IsTransientFileAccessError(error).ShouldEqual(expected);
        }

        [TestCase]
        public void IsTransientFileAccessErrorHonorsRequiredLinePhrase()
        {
            const string Error = "warning: could not open 'config': Permission denied";

            GitConfigReadRetry.IsTransientFileAccessError(Error).ShouldBeTrue();
            GitConfigReadRetry.IsTransientFileAccessError(Error, lineMustContain: "unable to access").ShouldBeFalse();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ConfigReaderDoesNotRetryTruncatedFailures(bool outputTruncated, bool errorsTruncated)
        {
            MockTracer tracer = new MockTracer();
            int reads = 0;
            int delays = 0;
            GitProcess.Result failure = new GitProcess.Result(
                string.Empty,
                "warning: unable to access 'config': Permission denied",
                128,
                outputTruncated,
                errorsTruncated);

            GitProcess.Result result = GitProcessConfigReader.Read(
                "credential",
                () =>
                {
                    ++reads;
                    return failure;
                },
                tracer,
                delay => ++delays);

            Assert.That(result, Is.SameAs(failure));
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(delays, Is.Zero);
            Assert.That(tracer.RelatedWarningEvents, Is.Empty);
        }

        [TestCase]
        public void InvokeResultStopsAtTheRequestedAttemptLimit()
        {
            MockTracer tracer = new MockTracer();
            int reads = 0;
            int delays = 0;

            int result = GitConfigReadRetry.InvokeResult(
                "gvfs.test",
                () => ++reads,
                (int _) => true,
                tracer,
                delay => ++delays,
                maxAttempts: 2);

            result.ShouldEqual(2);
            reads.ShouldEqual(2);
            delays.ShouldEqual(1);
        }

        [TestCase]
        public void InvokeResultStopsWhenTheCallerCannotWaitForTheDelay()
        {
            MockTracer tracer = new MockTracer();
            List<TimeSpan> requestedWaits = new List<TimeSpan>();
            List<int> retriedResults = new List<int>();
            int reads = 0;
            int delays = 0;

            GitConfigReadRetry.InvokeResult(
                "gvfs.test",
                () => ++reads,
                (int _) => true,
                tracer,
                delay => ++delays,
                canWait: wait =>
                {
                    requestedWaits.Add(wait);
                    return requestedWaits.Count < 3;
                },
                beforeRetry: retriedResults.Add);

            reads.ShouldEqual(3);
            delays.ShouldEqual(2);
            retriedResults.ShouldMatchInOrder(1, 2);
            requestedWaits.ShouldMatchInOrder(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80));
            tracer.RelatedWarningEvents.Count.ShouldEqual(3);
            tracer.RelatedWarningEvents[2].ShouldContain("persisted after all attempts");
        }

        [TestCase]
        public void InvokeResultDoesNotRetryWhenOneAttemptIsAllowed()
        {
            int reads = 0;
            int delays = 0;

            GitConfigReadRetry.InvokeResult(
                "gvfs.test",
                () => ++reads,
                (int _) => true,
                new MockTracer(),
                delay => ++delays,
                maxAttempts: 1);

            reads.ShouldEqual(1);
            delays.ShouldEqual(0);
        }

        [TestCase]
        public void InvokeResultUsesExponentialDelays()
        {
            List<TimeSpan> delays = new List<TimeSpan>();

            GitConfigReadRetry.InvokeResult(
                "gvfs.test",
                () => 0,
                (int _) => true,
                new MockTracer(),
                delays.Add);

            delays.ShouldMatchInOrder(
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(80),
                TimeSpan.FromMilliseconds(160));
        }

        [TestCase]
        public void InvokeCatchingRethrowsTheOriginalExceptionAfterTheLastAttempt()
        {
            int reads = 0;
            InvalidOperationException lastException = null;

            Assert.Throws<InvalidOperationException>(
                () => GitConfigReadRetry.InvokeCatching<int>(
                    "gvfs.test",
                    () =>
                    {
                        lastException = new InvalidOperationException("attempt " + ++reads);
                        throw lastException;
                    },
                    (Exception _) => true,
                    new MockTracer(),
                    delay => { },
                    maxAttempts: 3));

            reads.ShouldEqual(3);
            lastException.Message.ShouldEqual("attempt 3");
        }

        [TestCase]
        public void TracedConfigNameDoesNotContainUrlCredentials()
        {
            MockTracer tracer = new MockTracer();

            GitConfigReadRetry.InvokeResult(
                "gvfs.user:token@example.com.repo.cache-server-url",
                () => 0,
                (int _) => true,
                tracer,
                delay => { },
                maxAttempts: 2);

            tracer.RelatedWarningEvents.Count.ShouldEqual(2);
            foreach (string warning in tracer.RelatedWarningEvents)
            {
                warning.Contains("token").ShouldBeFalse();
                warning.ShouldContain("redacted");
            }
        }
    }
}
