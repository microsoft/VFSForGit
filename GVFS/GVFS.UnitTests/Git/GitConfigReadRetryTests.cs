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
