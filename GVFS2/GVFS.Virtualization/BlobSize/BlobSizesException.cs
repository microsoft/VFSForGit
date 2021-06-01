using System;

namespace GVFS.Virtualization.BlobSize
{
    public class BlobSizesException : Exception
    {
        public BlobSizesException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }
}
