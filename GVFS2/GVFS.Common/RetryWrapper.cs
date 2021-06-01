using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common
{
    public class RetryWrapper<T>
    {
        private const float MaxBackoffInSeconds = 300; // 5 minutes
        private readonly int maxAttempts;
        private readonly double exponentialBackoffBase;
        private readonly CancellationToken cancellationToken;

        public RetryWrapper(int maxAttempts, CancellationToken cancellationToken, double exponentialBackoffBase = RetryBackoff.DefaultExponentialBackoffBase)
        {
            this.maxAttempts = maxAttempts;
            this.cancellationToken = cancellationToken;
            this.exponentialBackoffBase = exponentialBackoffBase;
        }

        public event Action<ErrorEventArgs> OnFailure = delegate { };

        public static Action<ErrorEventArgs> StandardErrorHandler(ITracer tracer, long requestId, string actionName, bool forceLogAsWarning = false)
        {
            return eArgs =>
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("RequestId", requestId);
                metadata.Add("AttemptNumber", eArgs.TryCount);
                metadata.Add("Operation", actionName);
                metadata.Add("WillRetry", eArgs.WillRetry);
                string message = null;
                if (eArgs.Error != null)
                {
                    message = eArgs.Error.Message;
                    metadata.Add("Exception", eArgs.Error.ToString());

                    int innerCounter = 1;
                    Exception e = eArgs.Error.InnerException;
                    while (e != null)
                    {
                        metadata.Add("InnerException" + innerCounter++, e.ToString());
                        e = e.InnerException;
                    }
                }

                if (eArgs.WillRetry || forceLogAsWarning)
                {
                    tracer.RelatedWarning(metadata, message, Keywords.Network);
                }
                else
                {
                    tracer.RelatedError(metadata, message, Keywords.Network);
                }
            };
        }

        public InvocationResult Invoke(Func<int, CallbackResult> toInvoke)
        {
            // Use 1-based counting. This makes reporting look a lot nicer and saves a lot of +1s
            for (int tryCount = 1; tryCount <= this.maxAttempts; ++tryCount)
            {
                this.cancellationToken.ThrowIfCancellationRequested();

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
                // Don't wait after the last try. tryCount is 1-based, so last attempt is tryCount == maxAttempts
                if (tryCount > 1 && tryCount < this.maxAttempts)
                {
                    double backOffSeconds = RetryBackoff.CalculateBackoffSeconds(tryCount, MaxBackoffInSeconds, this.exponentialBackoffBase);
                    try
                    {
                        Task.Delay(TimeSpan.FromSeconds(backOffSeconds), this.cancellationToken).GetAwaiter().GetResult();
                    }
                    catch (TaskCanceledException)
                    {
                        throw new OperationCanceledException(this.cancellationToken);
                    }
                }
            }

            // This shouldn't be hit because ShouldRetry will cause a more useful message first.
            return new InvocationResult(this.maxAttempts, new Exception("Unexpected failure after retrying"));
        }

        private bool IsHandlableException(Exception e)
        {
            return
                e is HttpRequestException ||
                e is IOException ||
                e is RetryableException;
        }

        private bool ShouldRetry(int tryCount, Exception e, CallbackResult result)
        {
            bool willRetry = tryCount < this.maxAttempts &&
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
