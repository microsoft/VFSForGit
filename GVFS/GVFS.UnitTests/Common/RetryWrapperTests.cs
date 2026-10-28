using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common;
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
        [SetUp]
        public void SetUp()
        {
            RetryCircuitBreaker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RetryCircuitBreaker.Reset();
        }

        [TestCase]
        public void LocalRejectionsKeepEveryCustomCallbackAndUseTypedClassification()
        {
            RetryCircuitBreaker.Configure(1, 30000);
            RetryCircuitBreaker.RecordFailure();
            int failures = 0;
            RetryWrapper<bool> wrapper = new RetryWrapper<bool>(5, new CancellationToken(canceled: true));
            wrapper.OnFailure += args =>
            {
                ++failures;
                Assert.That(args.Error, Is.TypeOf<CircuitBreakerOpenException>());
                Assert.That(args.TryCount, Is.EqualTo(1));
                Assert.That(args.WillRetry, Is.False);
                EventMetadata metadata = new EventMetadata();
                args.AddTelemetryMetadata(metadata, "CustomDownload");
                Assert.That(metadata.TelemetryAggregation.FailureCategory, Is.EqualTo(TelemetryFailureCategory.CircuitBreakerOpen));
            };

            for (int i = 0; i < 100; ++i)
            {
                RetryWrapper<bool>.InvocationResult result = wrapper.Invoke(attempt => throw new AssertionException("Local rejection must not invoke the callback."));
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Attempts, Is.EqualTo(1));
                Assert.That(result.Result, Is.False);
            }

            Assert.That(failures, Is.EqualTo(100));
            Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.EqualTo(1));
        }

        [TestCase]
        public void StandardHandlerDoesNotClassifyMessageLookalikesAsLocalRejections()
        {
            MockTracer tracer = new MockTracer();
            Action<RetryWrapper<bool>.ErrorEventArgs> handler = RetryWrapper<bool>.StandardErrorHandler(tracer, 1, "Download");
            handler(new RetryWrapper<bool>.ErrorEventArgs(new IOException(new CircuitBreakerOpenException().Message), 1, false));
            Assert.That(tracer.RelatedErrorEvents[0], Does.Not.Contain("TelemetryAggregation"));

            handler(new RetryWrapper<bool>.ErrorEventArgs(new CircuitBreakerOpenException(), 1, false));
            Assert.That(tracer.RelatedErrorEvents[1], Does.Contain("\"FailureCategory\":\"CircuitBreakerOpen\""));
        }

        [TestCase]
        public void CooldownObservationIsNotReportedAsRecovery()
        {
            MockTracer tracer = new MockTracer();
            RetryCircuitBreaker.Configure(1, 30000);
            RetryCircuitBreaker.RecordFailure();
            RetryCircuitBreaker.TraceState(tracer, isOpen: true, "Failure");
            RetryCircuitBreaker.Configure(1, 0);

            RetryWrapper<bool> wrapper = new RetryWrapper<bool>(1, CancellationToken.None, tracer: tracer);
            int callbacks = 0;
            wrapper.Invoke(attempt =>
            {
                ++callbacks;
                return new RetryWrapper<bool>.CallbackResult(new IOException("No response"), shouldRetry: false);
            });

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(tracer.RelatedEventMetadata.Count, Is.EqualTo(2));
            Assert.That(tracer.RelatedEventMetadata[1]["State"], Is.EqualTo("CooldownElapsed"));
            Assert.That(RetryCircuitBreaker.ConsecutiveFailures, Is.EqualTo(1));

            wrapper.Invoke(attempt => new RetryWrapper<bool>.CallbackResult(true));
            Assert.That(tracer.RelatedEventMetadata[2]["State"], Is.EqualTo("Accepting"));
            Assert.That(tracer.RelatedEventMetadata[2]["Trigger"], Is.EqualTo("Success"));
        }

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

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CircuitBreakerOpensAfterConsecutiveFailures()
        {
            const int Threshold = 5;
            const int CooldownMs = 5000;
            RetryCircuitBreaker.Configure(Threshold, CooldownMs);

            // Generate enough failures to trip the circuit breaker
            for (int i = 0; i < Threshold; i++)
            {
                RetryWrapper<bool> wrapper = new RetryWrapper<bool>(1, CancellationToken.None, exponentialBackoffBase: 0);
                wrapper.Invoke(tryCount => throw new IOException("simulated failure"));
            }

            RetryCircuitBreaker.IsOpen.ShouldBeTrue("Circuit breaker should be open after threshold failures");

            // Next invocation should fail fast without calling the callback
            int callbackInvocations = 0;
            RetryWrapper<bool> dut = new RetryWrapper<bool>(5, CancellationToken.None, exponentialBackoffBase: 0);
            RetryWrapper<bool>.InvocationResult result = dut.Invoke(
                tryCount =>
                {
                    callbackInvocations++;
                    return new RetryWrapper<bool>.CallbackResult(true);
                });

            result.Succeeded.ShouldEqual(false);
            callbackInvocations.ShouldEqual(0);
        }

        [TestCase]
        public void CircuitBreakerResetsOnSuccess()
        {
            const int Threshold = 3;
            RetryCircuitBreaker.Configure(Threshold, 30_000);

            // Record failures just below threshold
            for (int i = 0; i < Threshold - 1; i++)
            {
                RetryCircuitBreaker.RecordFailure();
            }

            RetryCircuitBreaker.IsOpen.ShouldBeFalse("Circuit should still be closed below threshold");

            // A successful invocation resets the counter
            RetryWrapper<bool> dut = new RetryWrapper<bool>(1, CancellationToken.None, exponentialBackoffBase: 0);
            dut.Invoke(tryCount => new RetryWrapper<bool>.CallbackResult(true));

            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);

            // Now threshold more failures are needed to trip it again
            for (int i = 0; i < Threshold - 1; i++)
            {
                RetryCircuitBreaker.RecordFailure();
            }

            RetryCircuitBreaker.IsOpen.ShouldBeFalse("Circuit should still be closed after reset");
        }

        [TestCase]
        public void CircuitBreakerIgnoresNonRetryableErrors()
        {
            const int Threshold = 3;
            RetryCircuitBreaker.Configure(Threshold, 30_000);

            // Generate non-retryable failures (e.g., 404/400) — these should NOT count
            for (int i = 0; i < Threshold + 5; i++)
            {
                RetryWrapper<bool> wrapper = new RetryWrapper<bool>(1, CancellationToken.None, exponentialBackoffBase: 0);
                wrapper.Invoke(tryCount => new RetryWrapper<bool>.CallbackResult(new Exception("404 Not Found"), shouldRetry: false));
            }

            RetryCircuitBreaker.IsOpen.ShouldBeFalse("Non-retryable errors should not trip the circuit breaker");
            RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CircuitBreakerClosesAfterCooldown()
        {
            const int Threshold = 3;
            const int CooldownMs = 100; // Very short cooldown for testing
            RetryCircuitBreaker.Configure(Threshold, CooldownMs);

            // Trip the circuit breaker
            for (int i = 0; i < Threshold; i++)
            {
                RetryWrapper<bool> wrapper = new RetryWrapper<bool>(1, CancellationToken.None, exponentialBackoffBase: 0);
                wrapper.Invoke(tryCount => throw new IOException("simulated failure"));
            }

            RetryCircuitBreaker.IsOpen.ShouldBeTrue("Circuit should be open");

            // Wait for cooldown to expire
            Thread.Sleep(CooldownMs + 50);

            RetryCircuitBreaker.IsOpen.ShouldBeFalse("Circuit should be closed after cooldown");

            // Should be able to invoke successfully now
            int callbackInvocations = 0;
            RetryWrapper<bool> dut = new RetryWrapper<bool>(1, CancellationToken.None, exponentialBackoffBase: 0);
            RetryWrapper<bool>.InvocationResult result = dut.Invoke(
                tryCount =>
                {
                    callbackInvocations++;
                    return new RetryWrapper<bool>.CallbackResult(true);
                });

            result.Succeeded.ShouldEqual(true);
            callbackInvocations.ShouldEqual(1);
        }
    }
}
