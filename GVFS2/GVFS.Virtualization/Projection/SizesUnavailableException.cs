using System;

namespace GVFS.Virtualization.Projection
{
    public class SizesUnavailableException : Exception
    {
        public SizesUnavailableException(string message)
            : base(message)
        {
        }
    }
}
