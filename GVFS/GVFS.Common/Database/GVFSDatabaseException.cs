using System;

namespace GVFS.Common.Database
{
    public class GVFSDatabaseException : Exception
    {
        public GVFSDatabaseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
