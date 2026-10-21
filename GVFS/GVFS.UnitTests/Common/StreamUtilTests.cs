using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class StreamUtilTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void BodyCancellationDoesNotBecomeARetryOrCircuitFailure(bool greedy)
        {
            RetryCircuitBreaker.Reset();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using HttpResponseStream stream = new HttpResponseStream(
                new MemoryStream(new byte[] { 1 }), TimeSpan.FromSeconds(60), cancellation.Token);
            RetryWrapper<bool> retry = new RetryWrapper<bool>(1, cancellation.Token);
            try
            {
                Assert.Catch<OperationCanceledException>(() => retry.Invoke(attempt =>
                {
                    cancellation.Cancel();
                    if (greedy)
                    {
                        StreamUtil.TryReadGreedy(stream, new byte[1], 0, 1).ShouldEqual(1);
                    }
                    else
                    {
                        StreamUtil.CopyToWithBuffer(stream, Stream.Null);
                    }

                    return new RetryWrapper<bool>.CallbackResult(true);
                }));
                RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
                stream.ReadFailed.ShouldEqual(false);
            }
            finally
            {
                RetryCircuitBreaker.Reset();
            }
        }
    }
}
