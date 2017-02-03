using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FastFetch.Jobs.Data
{
    public class BlobDownloadRequest
    {
        private static int requestCounter = 0;

        public BlobDownloadRequest(IReadOnlyList<string> objectIds)
        {
            this.ObjectIds = objectIds;
            this.PackId = Interlocked.Increment(ref requestCounter);
        }

        public static int TotalRequests
        {
            get
            {
                return requestCounter;
            }
        }

        public IReadOnlyList<string> ObjectIds { get; }

        public int PackId { get; }
    }
}
