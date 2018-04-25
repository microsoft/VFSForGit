using System;

namespace GVFS.GVFlt.BlobSize
{
    public class BlobSizesException : Exception
    {
        public BlobSizesException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }
}
