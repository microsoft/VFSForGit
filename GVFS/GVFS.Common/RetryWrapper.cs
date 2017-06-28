using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace GVFS.Common
{
    public class RetryWrapper<T>
    {
        private const float MaxBackoffInSeconds = 300; // 5 minutes
        private const float DefaultExponentialBackoffBase = 2;

        private readonly int maxRetries;
        private readonly float exponentialBackoffBase;

        private Random rng = new Random();

        public RetryWrapper(int maxRetries, float exponentialBackoffBase = DefaultExponentialBackoffBase)
        {
            this.maxRetries = maxRetries;
            this.exponentialBackoffBase = exponentialBackoffBase;
        }

        public event Action<ErrorEventArgs> OnFailure = delegate { };

        public static Action<ErrorEventArgs> StandardErrorHandler(ITracer tracer, long requestId, string actionName)
        {
            return eArgs =>
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("RequestId", requestId);
                metadata.Add("AttemptNumber", eArgs.TryCount);
                metadata.Add("Operation", actionName);
                metadata.Add("WillRetry", eArgs.WillRetry);
                metadata.Add("ErrorMessage", eArgs.Error != null ? eArgs.Error.Message : null);
                tracer.RelatedError(metadata, Keywords.Network);

                // Emit with stack at a higher verbosity.
                metadata["ErrorMessage"] = eArgs.Error != null ? eArgs.Error.ToString() : null;
                tracer.RelatedEvent(EventLevel.Verbose, JsonEtwTracer.NetworkErrorEventName, metadata, Keywords.Network);
            };
        }

        public async Task<InvocationResult> InvokeAsync(Func<int, Task<CallbackResult>> toInvoke)
        {
            // Use 1-based counting. This makes reporting look a lot nicer and saves a lot of +1s
            for (int tryCount = 1; tryCount <= this.maxRetries; ++tryCount)
            {
                try
                {
                    CallbackResult result = await toInvoke(tryCount);
                    if (result.HasErrors)
                    {
                        if (!this.ShouldRetry(tryCount, null, result))
                        {
                            return new InvocationResult(tryCount, result.Error, result.Result);
                        }
                    }
                    else
                    {
                        return new InvocationResult(tryCount, true, result.Result);
                    }
                }
                catch (Exception e)
                {
                    Exception exceptionToReport =
                        e is AggregateException
                        ? ((AggregateException)e).Flatten().InnerException
                        : e;

                    if (!this.IsHandlableException(exceptionToReport))
                    {
                        throw;
                    }

                    if (!this.ShouldRetry(tryCount, exceptionToReport, null))
                    {
                        return new InvocationResult(tryCount, exceptionToReport);
                    }
                }

                // Don't wait for the first retry, since it might just be transient.
                // Don't wait after the last try. tryCount is 1-based, so last attempt is tryCount == maxRetries
                if (tryCount > 1 && tryCount < this.maxRetries)
                {
                    // Exponential backoff
                    double backOffSeconds = Math.Min(Math.Pow(this.exponentialBackoffBase, tryCount), MaxBackoffInSeconds);

                    // Timeout usually happens when the server is overloaded. If we give all machines the same timeout they will all make
                    // another request at approximately the same time causing the problem to happen again and again. To avoid that we
                    // introduce a random timeout. To avoid scaling it too high or too low, it is +- 10% of the average backoff
                    backOffSeconds *= .9 + (this.rng.NextDouble() * .2);
                    await Task.Delay(TimeSpan.FromSeconds(backOffSeconds));
                }
            }

            // This shouldn't be hit because ShouldRetry will cause a more useful message first.
            return new InvocationResult(this.maxRetries, new Exception("Unexpected failure after retrying"));
        }

        public InvocationResult Invoke(Func<int, CallbackResult> toInvoke)
        {
            // Use 1-based counting. This makes reporting look a lot nicer and saves a lot of +1s
            for (int tryCount = 1; tryCount <= this.maxRetries; ++tryCount)
            {
                try
                {
                    CallbackResult result = toInvoke(tryCount);
                    if (result.HasErrors)
                    {
                        if (!this.ShouldRetry(tryCount, null, result))
                        {
                            return new InvocationResult(tryCount, result.Error, result.Result);
                        }
                    }
                    else
                    {
                        return new InvocationResult(tryCount, true, result.Result);
                    }
                }
                catch (Exception e)
                {
                    Exception exceptionToReport =
                        e is AggregateException
                        ? ((AggregateException)e).Flatten().InnerException
                        : e;

                    if (!this.IsHandlableException(exceptionToReport))
                    {
                        throw;
                    }

                    if (!this.ShouldRetry(tryCount, exceptionToReport, null))
                    {
                        return new InvocationResult(tryCount, exceptionToReport);
                    }
                }

                // Don't wait for the first retry, since it might just be transient.
                // Don't wait after the last try. tryCount is 1-based, so last attempt is tryCount == maxRetries
                if (tryCount > 1 && tryCount < this.maxRetries)
                {
                    // Exponential backoff
                    double backOffSeconds = Math.Min(Math.Pow(this.exponentialBackoffBase, tryCount), MaxBackoffInSeconds);

                    // Timeout usually happens when the server is overloaded. If we give all machines the same timeout they will all make
                    // another request at approximately the same time causing the problem to happen again and again. To avoid that we
                    // introduce a random timeout. To avoid scaling it too high or too low, it is +- 10% of the average backoff
                    backOffSeconds *= .9 + (this.rng.NextDouble() * .2);
                    Thread.Sleep(TimeSpan.FromSeconds(backOffSeconds));
                }
            }

            // This shouldn't be hit because ShouldRetry will cause a more useful message first.
            return new InvocationResult(this.maxRetries, new Exception("Unexpected failure after retrying"));
        }

        private bool IsHandlableException(Exception e)
        {
            return
                e is HttpException ||
                e is HttpRequestException ||
                e is IOException ||
                e is RetryableException;
        }

        private bool ShouldRetry(int tryCount, Exception e, CallbackResult result)
        {
            bool willRetry = tryCount < this.maxRetries &&
                (result == null || result.ShouldRetry);

            if (e != null)
            {
                this.OnFailure(new ErrorEventArgs(e, tryCount, willRetry));
            }
            else
            {
                this.OnFailure(new ErrorEventArgs(result.Error, tryCount, willRetry));
            }

            return willRetry;
        }

        public class ErrorEventArgs
        {
            public ErrorEventArgs(Exception error, int tryCount, bool willRetry)
            {
                this.Error = error;
                this.TryCount = tryCount;
                this.WillRetry = willRetry;
            }

            public bool WillRetry { get; }

            public int TryCount { get; }

            public Exception Error { get; }
        }

        public class InvocationResult
        {
            public InvocationResult(int tryCount, bool succeeded, T result)
            {
                this.Attempts = tryCount;
                this.Succeeded = true;
                this.Result = result;
            }

            public InvocationResult(int tryCount, Exception error)
            {
                this.Attempts = tryCount;
                this.Succeeded = false;
                this.Error = error;
            }

            public InvocationResult(int tryCount, Exception error, T result)
                : this(tryCount, error)
            {
                this.Result = result;
            }

            public T Result { get; }
            public int Attempts { get; }
            public bool Succeeded { get; }
            public Exception Error { get; }
        }

        public class CallbackResult
        {
            public CallbackResult(T result)
            {
                this.Result = result;
            }

            public CallbackResult(Exception error, bool shouldRetry)
            {
                this.HasErrors = true;
                this.Error = error;
                this.ShouldRetry = shouldRetry;
            }

            public CallbackResult(Exception error, bool shouldRetry, T result)
                : this(error, shouldRetry)
            {
                this.Result = result;
            }

            public bool HasErrors { get; }
            public Exception Error { get; }
            public bool ShouldRetry { get; }
            public T Result { get; }
        }
    }
}
