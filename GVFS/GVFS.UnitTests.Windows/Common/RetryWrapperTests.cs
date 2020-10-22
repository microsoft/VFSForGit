using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RetryWrapperTests
    {
        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WillRetryOnIOException()
        {
            const int ExpectedTries = 5;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(ExpectedTries, CancellationToken.None, exponentialBackoffBase: 0);

            int actualTries = 0;
            RetryWrapper<bool>.InvocationResult output = dut.Invoke(
                tryCount =>
                {
                    actualTries++;
                    throw new IOException();
                });

            output.Succeeded.ShouldEqual(false);
            actualTries.ShouldEqual(ExpectedTries);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WillNotRetryForGenericExceptions()
        {
            const int MaxTries = 5;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, CancellationToken.None, exponentialBackoffBase: 0);

            Assert.Throws<Exception>(
                () =>
                {
                    RetryWrapper<bool>.InvocationResult output = dut.Invoke(tryCount => { throw new Exception(); });
                });
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WillNotMakeAnyAttemptWhenInitiallyCanceled()
        {
            const int MaxTries = 5;
            int actualTries = 0;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, new CancellationToken(canceled: true), exponentialBackoffBase: 0);

            Assert.Throws<OperationCanceledException>(
                () =>
                {
                    RetryWrapper<bool>.InvocationResult output = dut.Invoke(tryCount =>
                    {
                        ++actualTries;
                        return new RetryWrapper<bool>.CallbackResult(true);
                    });
                });

            actualTries.ShouldEqual(0);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WillNotRetryForWhenCanceledDuringAttempts()
        {
            const int MaxTries = 5;
            int actualTries = 0;
            int expectedTries = 3;

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, tokenSource.Token, exponentialBackoffBase: 0);

                Assert.Throws<OperationCanceledException>(
                    () =>
                    {
                        RetryWrapper<bool>.InvocationResult output = dut.Invoke(tryCount =>
                        {
                            ++actualTries;

                            if (actualTries == expectedTries)
                            {
                                tokenSource.Cancel();
                            }

                            return new RetryWrapper<bool>.CallbackResult(new Exception("Test"), shouldRetry: true);
                        });
                    });

                actualTries.ShouldEqual(expectedTries);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WillNotRetryWhenCancelledDuringBackoff()
        {
            const int MaxTries = 5;
            int actualTries = 0;
            int expectedTries = 2; // 2 because RetryWrapper does not wait after the first failure

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, tokenSource.Token, exponentialBackoffBase: 300);

                Task.Run(() =>
                {
                    // Wait 3 seconds and cancel
                    Thread.Sleep(1000 * 3);
                    tokenSource.Cancel();
                });

                Assert.Throws<OperationCanceledException>(
                    () =>
                    {
                        RetryWrapper<bool>.InvocationResult output = dut.Invoke(tryCount =>
                        {
                            ++actualTries;
                            return new RetryWrapper<bool>.CallbackResult(new Exception("Test"), shouldRetry: true);
                        });
                    });

                actualTries.ShouldEqual(expectedTries);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnFailureIsCalledWhenEventHandlerAttached()
        {
            const int MaxTries = 5;
            const int ExpectedFailures = 5;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, CancellationToken.None, exponentialBackoffBase: 0);

            int actualFailures = 0;
            dut.OnFailure += errorArgs => actualFailures++;

            RetryWrapper<bool>.InvocationResult output = dut.Invoke(
                tryCount =>
                {
                    throw new IOException();
                });

            output.Succeeded.ShouldEqual(false);
            actualFailures.ShouldEqual(ExpectedFailures);
        }

        [TestCase]
        public void OnSuccessIsOnlyCalledOnce()
        {
            const int MaxTries = 5;
            const int ExpectedFailures = 0;
            const int ExpectedTries = 1;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, CancellationToken.None, exponentialBackoffBase: 0);

            int actualFailures = 0;
            dut.OnFailure += errorArgs => actualFailures++;

            int actualTries = 0;
            RetryWrapper<bool>.InvocationResult output = dut.Invoke(
                tryCount =>
                {
                    actualTries++;
                    return new RetryWrapper<bool>.CallbackResult(true);
                });

            output.Succeeded.ShouldEqual(true);
            output.Result.ShouldEqual(true);
            actualTries.ShouldEqual(ExpectedTries);
            actualFailures.ShouldEqual(ExpectedFailures);
        }

        [TestCase]
        public void WillNotRetryWhenNotRequested()
        {
            const int MaxTries = 5;
            const int ExpectedFailures = 1;
            const int ExpectedTries = 1;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, CancellationToken.None, exponentialBackoffBase: 0);

            int actualFailures = 0;
            dut.OnFailure += errorArgs => actualFailures++;

            int actualTries = 0;
            RetryWrapper<bool>.InvocationResult output = dut.Invoke(
                tryCount =>
                {
                    actualTries++;
                    return new RetryWrapper<bool>.CallbackResult(new Exception("Test"), shouldRetry: false);
                });

            output.Succeeded.ShouldEqual(false);
            output.Result.ShouldEqual(false);
            actualTries.ShouldEqual(ExpectedTries);
            actualFailures.ShouldEqual(ExpectedFailures);
        }

        [TestCase]
        public void WillRetryWhenRequested()
        {
            const int MaxTries = 5;
            const int ExpectedFailures = 5;
            const int ExpectedTries = 5;

            RetryWrapper<bool> dut = new RetryWrapper<bool>(MaxTries, CancellationToken.None, exponentialBackoffBase: 0);

            int actualFailures = 0;
            dut.OnFailure += errorArgs => actualFailures++;

            int actualTries = 0;
            RetryWrapper<bool>.InvocationResult output = dut.Invoke(
                tryCount =>
                {
                    actualTries++;
                    return new RetryWrapper<bool>.CallbackResult(new Exception("Test"), shouldRetry: true);
                });

            output.Succeeded.ShouldEqual(false);
            output.Result.ShouldEqual(false);
            actualTries.ShouldEqual(ExpectedTries);
            actualFailures.ShouldEqual(ExpectedFailures);
        }
    }
}
