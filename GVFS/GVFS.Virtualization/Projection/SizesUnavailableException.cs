using System;

namespace GVFS.Virtualization.Projection
{
    public class SizesUnavailableException : Exception
    {
        public SizesUnavailableException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
