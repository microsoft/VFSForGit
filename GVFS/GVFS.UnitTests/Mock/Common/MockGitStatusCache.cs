using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockGitStatusCache : GitStatusCache
    {
        public MockGitStatusCache(GVFSContext context, TimeSpan backoff)
            : base(context, backoff)
        {
        }

        public int InvalidateCallCount { get; private set; }

        public void ResetCalls()
        {
            this.InvalidateCallCount = 0;
        }

        public override void Dispose()
        {
        }

        public override void Initialize()
        {
        }

        public override void Invalidate()
        {
            this.InvalidateCallCount++;
        }

        public override bool IsReadyForExternalAcquireLockRequests(NamedPipeMessages.LockData requester, out string infoMessage)
        {
            infoMessage = string.Empty;
            return true;
        }

        public override bool IsCacheReadyAndUpToDate()
        {
            return false;
        }

        public override void RefreshAsynchronously()
        {
        }

        public override void Shutdown()
        {
        }

        public override bool WriteTelemetryandReset(EventMetadata metadata)
        {
            return false;
        }
    }
}
