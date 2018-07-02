using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RetryBackoffTests
    {
        [TestCase]
        public void CalculateBackoffReturnsZeroForFirstAttempt()
        {
            int failedAttempt = 1;
            int maxBackoff = 300;

            RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff).ShouldEqual(0);
        }

        [TestCase]
        public void CalculateBackoff()
        {
            int failedAttempt = 2;
            int maxBackoff = 300;

            double backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff);
            this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase);

            backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase + 1);
            this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase + 1);

            ++failedAttempt;
            backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff);
            this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase);

            backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase + 1);
            this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase + 1);
        }

        [TestCase]
        public void CalculateBackoffThatWouldExceedMaxBackoff()
        {
            int failedAttempt = 30;
            int maxBackoff = 300;
            double backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff);
            this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase);
        }

        [TestCase]
        public void CalculateBackoffAcrossMultipleThreads()
        {
            int failedAttempt = 2;
            int maxBackoff = 300;
            int numThreads = 10;

            Thread[] calcThreads = new Thread[numThreads];

            for (int i = 0; i < numThreads; i++)
            {
                calcThreads[i] = new Thread(
                    () =>
                    {
                        double backoff = RetryBackoff.CalculateBackoffSeconds(failedAttempt, maxBackoff);
                        this.ValidateBackoff(backoff, failedAttempt, maxBackoff, RetryBackoff.DefaultExponentialBackoffBase);
                    });

                calcThreads[i].Start();
            }

            for (int i = 0; i < calcThreads.Length; i++)
            {
                calcThreads[i].Join();
            }
        }

        private void ValidateBackoff(double backoff, int failedAttempt, double maxBackoff, double exponentialBackoffBase)
        {
            backoff.ShouldBeAtLeast(Math.Min(Math.Pow(exponentialBackoffBase, failedAttempt), maxBackoff) * .9);
            backoff.ShouldBeAtMost(Math.Min(Math.Pow(exponentialBackoffBase, failedAttempt), maxBackoff) * 1.1);
        }
    }
}
