using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Common;
using GVFS.Virtualization.BlobSize;
using System;

namespace GVFS.UnitTests.Mock.Virtualization.BlobSize
{
    public class MockBlobSizes : BlobSizes
    {
        public MockBlobSizes()
            : base("mock:\\blobSizeDatabase", fileSystem: null, tracer: new MockTracer())
        {
        }

        public override void Initialize()
        {
        }

        public override void Shutdown()
        {
        }

        public override BlobSizesConnection CreateConnection()
        {
            return new MockBlobSizesConnection(this);
        }

        public override void AddSize(Sha1Id sha, long length)
        {
            throw new NotSupportedException("SaveValue has not been implemented yet.");
        }

        public override void Flush()
        {
            throw new NotSupportedException("Flush has not been implemented yet.");
        }

        public class MockBlobSizesConnection : BlobSizesConnection
        {
            public MockBlobSizesConnection(MockBlobSizes mockBlobSizesDatabase)
                : base(mockBlobSizesDatabase)
            {
            }

            public override bool TryGetSize(Sha1Id sha, out long length)
            {
                throw new NotSupportedException("TryGetSize has not been implemented yet.");
            }
        }
    }
}
