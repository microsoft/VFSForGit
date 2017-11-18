using System;
using System.IO;

namespace RGFS.Common.NamedPipes
{
    public class BrokenPipeException : Exception
    {
        public BrokenPipeException(string message, IOException innerException)
            : base(message, innerException)
        {
        }
    }
}
