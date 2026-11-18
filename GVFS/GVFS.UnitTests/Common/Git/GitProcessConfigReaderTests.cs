using GVFS.Common.Git;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    [Category(CategoryConstants.ExceptionExpected)]
    public class GitProcessConfigReaderTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationDoesNotKillTheTreeAgainWhileTheProcessIsExiting(bool readCompletesDuringKill)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TaskCompletionSource<GitProcess.Result> read = new TaskCompletionSource<GitProcess.Result>();
            int killAttempts = 0;

            Task<GitProcess.Result> result = GitProcessConfigReader.ReadWithProcessCleanupAsync(
                () => read.Task,
                () => false,
                () =>
                {
                    if (Interlocked.Increment(ref killAttempts) > 1)
                    {
                        throw new AggregateException(new Win32Exception(5));
                    }

                    if (readCompletesDuringKill)
                    {
                        read.SetCanceled(cancellation.Token);
                    }
                },
                cancellation.Token);

            cancellation.Cancel();
            if (!readCompletesDuringKill)
            {
                read.SetCanceled(cancellation.Token);
            }

            Assert.CatchAsync<OperationCanceledException>(async () => await result.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(killAttempts, Is.EqualTo(1));
        }

        [Test]
        public void FailedReadAndFailedCleanupPreserveBothErrors()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TaskCompletionSource<GitProcess.Result> read = new TaskCompletionSource<GitProcess.Result>();
            IOException readFailure = new IOException();
            Win32Exception killFailure = new Win32Exception(5);
            Task<GitProcess.Result> result = GitProcessConfigReader.ReadWithProcessCleanupAsync(
                () => read.Task,
                () => true,
                () => throw killFailure,
                cancellation.Token);

            Assert.DoesNotThrow(cancellation.Cancel);
            read.SetException(readFailure);

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () => await result);
            Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { readFailure, killFailure }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedCancellationKillRetriesAnActiveProcessAndKeepsTheErrors(bool retryFails)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TaskCompletionSource<GitProcess.Result> read = new TaskCompletionSource<GitProcess.Result>();
            Win32Exception failure = new Win32Exception(5);
            Win32Exception retryFailure = new Win32Exception(6);
            int killAttempts = 0;
            Task<GitProcess.Result> result = GitProcessConfigReader.ReadWithProcessCleanupAsync(
                () => read.Task,
                () => false,
                () =>
                {
                    if (++killAttempts == 1)
                    {
                        throw failure;
                    }

                    if (retryFails)
                    {
                        throw retryFailure;
                    }
                },
                cancellation.Token);

            Assert.DoesNotThrow(cancellation.Cancel);
            read.SetCanceled(cancellation.Token);

            if (retryFails)
            {
                AggregateException combined = Assert.ThrowsAsync<AggregateException>(async () => await result);
                Assert.That(combined.InnerExceptions, Is.EqualTo(new Exception[] { failure, retryFailure }));
            }
            else
            {
                Assert.That(Assert.ThrowsAsync<Win32Exception>(async () => await result), Is.SameAs(failure));
            }

            Assert.That(killAttempts, Is.EqualTo(2));
        }

        [Test]
        public void CancellationBeforeRegistrationDoesNotKillTheTreeAgain()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int killAttempts = 0;
            Task<GitProcess.Result> result = GitProcessConfigReader.ReadWithProcessCleanupAsync(
                () => Task.FromCanceled<GitProcess.Result>(cancellation.Token),
                () => false,
                () => ++killAttempts,
                cancellation.Token);

            Assert.CatchAsync<OperationCanceledException>(async () => await result.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(killAttempts, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationReportsTheOriginalKillFailure(bool isTreeFailure)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TaskCompletionSource<GitProcess.Result> read = new TaskCompletionSource<GitProcess.Result>();
            Win32Exception nativeFailure = new Win32Exception(5);
            Exception failure = isTreeFailure ? new AggregateException(nativeFailure, new Win32Exception(6)) : nativeFailure;
            Task<GitProcess.Result> result = GitProcessConfigReader.ReadWithProcessCleanupAsync(
                () => read.Task,
                () => true,
                () => throw failure,
                cancellation.Token);

            Assert.DoesNotThrow(cancellation.Cancel);
            read.SetCanceled(cancellation.Token);

            Assert.That(Assert.CatchAsync(async () => await result), Is.SameAs(failure));
        }
    }
}
