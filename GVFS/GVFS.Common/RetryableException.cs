using System;

namespace GVFS.Common
{
    public class RetryableException : Exception
    {
        public RetryableException(string message) : base(message)
        {
        }
    }
}
