using System;

namespace RGFS.Common
{
    public class InvalidRepoException : Exception
    {
        public InvalidRepoException(string message)
            : base(message)
        {
        }
    }
}
