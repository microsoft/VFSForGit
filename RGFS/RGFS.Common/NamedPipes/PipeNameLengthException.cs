using System;

namespace RGFS.Common.NamedPipes
{
    public class PipeNameLengthException : Exception
    {
        public PipeNameLengthException(string message)
            : base(message)
        {
        }
    }
}
