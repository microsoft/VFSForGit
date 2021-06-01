using System.Collections.Generic;
using System.Threading;

namespace GVFS.Common.Prefetch.Pipeline.Data
{
    public class BlobDownloadRequest
    {
        private static int requestCounter = 0;

        public BlobDownloadRequest(IReadOnlyList<string> objectIds)
        {
            this.ObjectIds = objectIds;
            this.RequestId = Interlocked.Increment(ref requestCounter);
        }

        public static int TotalRequests
        {
            get
            {
                return requestCounter;
            }
        }

        public IReadOnlyList<string> ObjectIds { get; }

        public int RequestId { get; }
    }
}
