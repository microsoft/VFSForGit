using System;

namespace GVFS.Common
{
    public class InvalidRepoException : Exception
    {
        public InvalidRepoException(string message)
            : base(message)
        {
        }
    }
}
