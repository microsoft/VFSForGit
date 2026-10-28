namespace GVFS.Common
{
    public class CircuitBreakerOpenException : RetryableException
    {
        public CircuitBreakerOpenException()
            : base("Circuit breaker is open - too many consecutive failures. Fast-failing to prevent resource exhaustion.")
        {
        }
    }
}
