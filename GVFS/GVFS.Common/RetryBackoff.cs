using System;

namespace GVFS.Common
{
    public static class RetryBackoff
    {
        public const double DefaultExponentialBackoffBase = 2;

        [ThreadStatic]
        private static Random threadLocalRandom;

        private static Random ThreadLocalRandom
        {
            get
            {
                if (threadLocalRandom == null)
                {
                    threadLocalRandom = new Random();
                }

                return threadLocalRandom;
            }
        }

        /// <summary>
        /// Computes the next backoff value in seconds.
        /// </summary>
        /// <param name="currentFailedAttempt">
        /// Current failed attempt using 1-based counting. (i.e. currentFailedAttempt should be 1 if the first attempt just failed
        /// </param>
        /// <param name="maxBackoffSeconds">Maximum allowed backoff</param>
        /// <returns>Time to backoff in seconds</returns>
        /// <remarks>Computed backoff is randomly adjusted by +- 10% to help prevent clients from hitting servers at the same time</remarks>
        public static double CalculateBackoffSeconds(int currentFailedAttempt, double maxBackoffSeconds, double exponentialBackoffBase = DefaultExponentialBackoffBase)
        {
            if (currentFailedAttempt <= 1)
            {
                return 0;
            }

            // Exponential backoff
            double backOffSeconds = Math.Min(Math.Pow(exponentialBackoffBase, currentFailedAttempt), maxBackoffSeconds);

            // Timeout usually happens when the server is overloaded. If we give all machines the same timeout they will all make
            // another request at approximately the same time causing the problem to happen again and again. To avoid that we
            // introduce a random timeout. To avoid scaling it too high or too low, it is +- 10% of the average backoff
            backOffSeconds *= .9 + (ThreadLocalRandom.NextDouble() * .2);
            return backOffSeconds;
        }
    }
}
